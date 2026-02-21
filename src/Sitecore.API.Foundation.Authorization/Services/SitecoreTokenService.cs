using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
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
    private readonly HttpClient _httpClient;
    
    private readonly ISitecoreTokenCache _tokenCache;
    private readonly SitecoreTokenServiceOptions _options;
    private readonly ILogger<SitecoreTokenService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SitecoreTokenService"/> class.
    /// </summary>
    /// <param name="httpClient">The <see cref="HttpClient"/> to use for making requests.</param>
    /// <param name="options">The configuration options for the token service.</param>
    /// <param name="tokenCache">The cache for storing and retrieving tokens.</param>
    /// <param name="logger">The logger for logging information and errors.</param>
    public SitecoreTokenService(
        HttpClient httpClient,
        IOptions<SitecoreTokenServiceOptions> options,
        ISitecoreTokenCache tokenCache,
        ILogger<SitecoreTokenService> logger)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _tokenCache = tokenCache ?? throw new ArgumentNullException(nameof(tokenCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SitecoreAuthToken> GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
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
        using var response = await _httpClient.PostAsJsonAsync(_options.AuthTokenUrl, authRequest, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("Auth response status: {StatusCode} {StatusText}.", (int)response.StatusCode, response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            var bodyError = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            _logger?.LogWarning("Authentication request failed with status {StatusCode} for {AuthUrl}. Body: {Body}", (int)response.StatusCode, _options.AuthTokenUrl, bodyError);
            throw new SitecoreAuthHttpException((int)response.StatusCode, _options.AuthTokenUrl,
                $"Failed to get auth token. Received {response.StatusCode} from {_options.AuthTokenUrl}.");
        }

        // Read raw first to distinguish empty/null vs parse errors
        var rawContent = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(rawContent) || rawContent == "null")
        {
            _logger?.LogError("Authentication response was empty for clientId {ClientId}. Raw: {Raw}", credentials.ClientId, rawContent);
            throw new SitecoreAuthResponseException("Failed to read auth token from response");
        }

        AuthResponse? result;
        try
        {
            result = System.Text.Json.JsonSerializer.Deserialize<AuthResponse>(rawContent);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to parse authentication response for clientId {ClientId}. Raw: {Raw}", credentials.ClientId, rawContent);
            throw new SitecoreAuthResponseException("Failed to parse auth response.", ex);
        }

        if (result is null || string.IsNullOrEmpty(result.access_token))
        {
            _logger?.LogError("Authentication response was empty or missing access_token for clientId {ClientId}. Raw: {Raw}", credentials.ClientId, rawContent);
            throw new SitecoreAuthResponseException("Failed to read auth token from response or token is not set.");
        }

        var goodUntil = DateTimeOffset.UtcNow.AddSeconds(result.expires_in);
        var sitecoreToken = new SitecoreAuthToken(result.access_token, goodUntil);
        _tokenCache.SetToken(credentials, sitecoreToken);
        _logger?.LogInformation("Token acquired and cached until {Expiration:o} for clientId {ClientId}.", sitecoreToken.Expiration, credentials.ClientId);
        return sitecoreToken;
    }

    /// <summary>
    /// Refreshes an existing Sitecore authentication token by re-authenticating with the same credentials.
    /// The old token is removed from the cache and a new token is generated.
    /// </summary>
    /// <param name="token">The token to refresh.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the new authentication token.</returns>
    /// <exception cref="ArgumentException">Thrown when the token is not managed by this service.</exception>
    /// <exception cref="SitecoreAuthHttpException">Thrown when the HTTP request fails or returns an error status code.</exception>
    /// <exception cref="SitecoreAuthResponseException">Thrown when the authentication response cannot be parsed or is invalid.</exception>
    public async Task<SitecoreAuthToken> TryRefreshSitecoreAuthToken(SitecoreAuthToken token, CancellationToken cancellationToken = default)
    {
        var credentials = _tokenCache.RemoveToken(token);
        if (!credentials.HasValue)
        {
            _logger?.LogWarning("Attempted to refresh a token not managed by the service.");
            throw new ArgumentException("The provided token is not managed by this service.", nameof(token));
        }
        
        return await GetSitecoreAuthToken(credentials.Value, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        try
        {
            if (response.Content == null) return string.Empty;
            var s = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return s ?? string.Empty;
        }
        catch
        {
            return string.Empty; // treat unreadable as empty so tests classify as read failure rather than parse failure
        }
    }

    private class AuthResponse
    {
        public string? access_token { get; set; }
        public int expires_in { get; set; }
    }
}