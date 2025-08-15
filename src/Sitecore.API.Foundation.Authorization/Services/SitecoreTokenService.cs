using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.Exceptions;
using Sitecore.API.Foundation.Authorization.Models;

namespace Sitecore.API.Foundation.Authorization.Services;

/// <summary>
/// Service for creating and managing Sitecore authentication tokens with automatic caching and cleanup.
/// </summary>
public class SitecoreTokenService : ISitecoreTokenService
{
    /// <summary>
    /// Gets the HTTP client used for authentication requests.
    /// </summary>
    protected readonly HttpClient HttpClient;
    
    private readonly ISitecoreTokenCache _tokenCache;
    private readonly SitecoreTokenServiceOptions _options;
    private readonly ILogger<SitecoreTokenService>? _logger;

    private class AuthResponse
    {
        public string? access_token { get; set; }
        public int expires_in { get; set; }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SitecoreTokenService"/> class.
    /// </summary>
    public SitecoreTokenService(
        HttpClient httpClient,
        ISitecoreTokenCache tokenCache,
        IOptions<SitecoreTokenServiceOptions> options,
        ILogger<SitecoreTokenService>? logger = null) 
    {
        HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _tokenCache = tokenCache ?? throw new ArgumentNullException(nameof(tokenCache));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    /// <summary>
    /// Gets a Sitecore authentication token for the specified credentials.
    /// Tokens are cached and reused until they expire. Automatic cleanup of expired tokens is performed periodically.
    /// </summary>
    /// <param name="credentials">The client credentials to authenticate with.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the authentication token.</returns>
    /// <exception cref="SitecoreAuthHttpException">Thrown when the HTTP request fails or returns an error status code.</exception>
    /// <exception cref="SitecoreAuthResponseException">Thrown when the authentication response cannot be parsed or is invalid.</exception>
    public async Task<SitecoreAuthToken> GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials)
    {
        // Check for cached token
        var cachedToken = _tokenCache.GetToken(credentials);
        if (cachedToken.HasValue)
        {
            _logger?.LogInformation("Token cache hit for clientId {ClientId}.", credentials.ClientId);
            return cachedToken.Value;
        }

        _logger?.LogInformation("Requesting new token for clientId {ClientId} from {AuthUrl}.", credentials.ClientId, _options.AuthTokenUrl);
        _logger?.LogDebug("Auth request payload: audience={Audience}, grant_type={GrantType}, client_id={ClientId}.", Constants.SitecoreAuthAudience, Constants.SitecoreAuthGrantType, credentials.ClientId);

        var authRequest = new
        {
            audience = Constants.SitecoreAuthAudience,
            grant_type = Constants.SitecoreAuthGrantType,
            client_id = credentials.ClientId,
            client_secret = credentials.ClientSecret,
        };
        
        using var response = await HttpClient.PostAsJsonAsync(_options.AuthTokenUrl, authRequest);
        _logger?.LogDebug("Auth response status: {StatusCode} {StatusText}.", (int)response.StatusCode, response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            var body = await SafeReadBodyAsync(response);
            _logger?.LogWarning("Authentication request failed with status {StatusCode} for {AuthUrl}. Body: {Body}", (int)response.StatusCode, _options.AuthTokenUrl, body);
            throw new SitecoreAuthHttpException((int)response.StatusCode, _options.AuthTokenUrl,
                $"Failed to get auth token. Received {response.StatusCode} from {_options.AuthTokenUrl}.");
        }
        
        AuthResponse? result;
        try
        {
            result = await response.Content.ReadFromJsonAsync<AuthResponse>();
        }
        catch (Exception ex)
        {
            var raw = await SafeReadBodyAsync(response);
            _logger?.LogError(ex, "Failed to parse authentication response for clientId {ClientId}. Raw: {Raw}", credentials.ClientId, raw);
            throw new SitecoreAuthResponseException("Failed to parse auth response.", ex);
        }
        
        if (result is null || string.IsNullOrEmpty(result.access_token))
        {
            var raw = await SafeReadBodyAsync(response);
            _logger?.LogError("Authentication response was empty or missing access_token for clientId {ClientId}. Raw: {Raw}", credentials.ClientId, raw);
            throw new SitecoreAuthResponseException("Failed to read auth token from response or token is not set.");
        }
        
        var goodUntil = DateTimeOffset.UtcNow.AddSeconds(result.expires_in);
        var sitecoreToken = new SitecoreAuthToken(result.access_token, goodUntil);
        
        // Cache the new token
        _tokenCache.SetToken(credentials, sitecoreToken);
        _logger?.LogInformation("Token acquired and cached until {Expiration:o} for clientId {ClientId}.", sitecoreToken.Expiration, credentials.ClientId);
        
        return sitecoreToken;
    }

    /// <summary>
    /// Refreshes an existing Sitecore authentication token by re-authenticating with the same credentials.
    /// The old token is removed from the cache and a new token is generated.
    /// </summary>
    /// <param name="token">The token to refresh.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the new authentication token.</returns>
    /// <exception cref="ArgumentException">Thrown when the token is not managed by this service.</exception>
    /// <exception cref="SitecoreAuthHttpException">Thrown when the HTTP request fails or returns an error status code.</exception>
    /// <exception cref="SitecoreAuthResponseException">Thrown when the authentication response cannot be parsed or is invalid.</exception>
    public async Task<SitecoreAuthToken> TryRefreshSitecoreAuthToken(SitecoreAuthToken token)
    {
        var credentials = _tokenCache.RemoveToken(token);
        if (!credentials.HasValue)
        {
            _logger?.LogWarning("Attempted to refresh a token not managed by the service.");
            throw new ArgumentException("The provided token is not managed by this service.", nameof(token));
        }
        
        return await GetSitecoreAuthToken(credentials.Value);
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadAsStringAsync();
        }
        catch
        {
            return "<unavailable>";
        }
    }
}