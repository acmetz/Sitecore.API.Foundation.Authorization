using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
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
    /// <summary>
    /// Gets the HTTP client used for authentication requests.
    /// </summary>
    protected readonly HttpClient HttpClient;
    
    private readonly ISitecoreTokenCache _tokenCache;
    private readonly SitecoreTokenServiceOptions _options;
    private readonly ILogger<SitecoreTokenService>? _logger;

    private sealed class AuthResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
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
    /// <exception cref="SitecoreAuthException">Thrown when the configured authentication endpoint URL is invalid or insecure.</exception>
    /// <exception cref="SitecoreAuthHttpException">Thrown when the HTTP request fails or returns an error status code.</exception>
    /// <exception cref="SitecoreAuthResponseException">Thrown when the authentication response cannot be parsed or is invalid.</exception>
    public Task<SitecoreAuthToken> GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials)
        => GetSitecoreAuthToken(credentials, CancellationToken.None);

    /// <summary>
    /// Gets a Sitecore authentication token for the specified credentials.
    /// Tokens are cached and reused until they expire. Automatic cleanup of expired tokens is performed periodically.
    /// </summary>
    /// <param name="credentials">The client credentials to authenticate with.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the authentication token.</returns>
    /// <exception cref="TaskCanceledException">Thrown when the operation is canceled via <paramref name="cancellationToken"/>.</exception>
    /// <exception cref="SitecoreAuthException">Thrown when the configured authentication endpoint URL is invalid or insecure.</exception>
    /// <exception cref="SitecoreAuthHttpException">Thrown when the HTTP request fails or returns an error status code.</exception>
    /// <exception cref="SitecoreAuthResponseException">Thrown when the authentication response cannot be parsed or is invalid.</exception>
    public async Task<SitecoreAuthToken> GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            throw new TaskCanceledException(null, null, cancellationToken);
        }

        // Check for cached token
        var cachedToken = _tokenCache.GetToken(credentials);
        if (cachedToken.HasValue)
        {
            _logger?.LogInformation("Token cache hit for clientId {ClientId}.", credentials.ClientId);
            return cachedToken.Value;
        }

        if (!Uri.TryCreate(_options.AuthTokenUrl, UriKind.Absolute, out var requestUri) ||
            !string.IsNullOrEmpty(requestUri.UserInfo) ||
            (requestUri.Scheme != Uri.UriSchemeHttps && !(requestUri.Scheme == Uri.UriSchemeHttp && requestUri.IsLoopback)))
        {
            _logger?.LogWarning(
                "Invalid or insecure AuthTokenUrl configured for clientId {ClientId}. AuthTokenUrl must be an absolute HTTPS URI (or loopback HTTP URI) without embedded credentials.",
                credentials.ClientId);
            throw new SitecoreAuthException("AuthTokenUrl must be an absolute HTTPS URI (or loopback HTTP URI) without embedded credentials.");
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
        
        using var response = await HttpClient.PostAsJsonAsync(_options.AuthTokenUrl, authRequest, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("Auth response status: {StatusCode} {StatusText}.", (int)response.StatusCode, response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogWarning("Authentication request failed with status {StatusCode} for {AuthUrl}.", (int)response.StatusCode, _options.AuthTokenUrl);
            throw new SitecoreAuthHttpException((int)response.StatusCode, _options.AuthTokenUrl,
                $"Failed to get auth token. Received {response.StatusCode} from {_options.AuthTokenUrl}.");
        }
        
        AuthResponse? result;
        try
        {
            result = await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to parse authentication response for clientId {ClientId}.", credentials.ClientId);
            throw new SitecoreAuthResponseException("Failed to parse auth response.", ex);
        }
        
        if (result is null || string.IsNullOrEmpty(result.AccessToken))
        {
            _logger?.LogError("Authentication response was empty or missing access_token for clientId {ClientId}.", credentials.ClientId);
            throw new SitecoreAuthResponseException("Failed to read auth token from response or token is not set.");
        }

        if (result.ExpiresIn <= 0)
        {
            _logger?.LogError("Auth response contained non-positive expires_in ({ExpiresIn}) for clientId {ClientId}.", result.ExpiresIn, credentials.ClientId);
            throw new SitecoreAuthResponseException($"Auth response contained non-positive expires_in ({result.ExpiresIn}).");
        }

        var goodUntil = DateTimeOffset.UtcNow.AddSeconds(result.ExpiresIn);
        var sitecoreToken = new SitecoreAuthToken(result.AccessToken, goodUntil);

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
    /// <exception cref="SitecoreAuthException">Thrown when the configured authentication endpoint URL is invalid or insecure.</exception>
    /// <exception cref="SitecoreAuthHttpException">Thrown when the HTTP request fails or returns an error status code.</exception>
    /// <exception cref="SitecoreAuthResponseException">Thrown when the authentication response cannot be parsed or is invalid.</exception>
    public Task<SitecoreAuthToken> TryRefreshSitecoreAuthToken(SitecoreAuthToken token)
        => TryRefreshSitecoreAuthToken(token, CancellationToken.None);

    /// <summary>
    /// Refreshes an existing Sitecore authentication token by re-authenticating with the same credentials.
    /// The old token is removed from the cache and a new token is generated.
    /// </summary>
    /// <param name="token">The token to refresh.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the new authentication token.</returns>
    /// <exception cref="TaskCanceledException">Thrown when the operation is canceled via <paramref name="cancellationToken"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the token is not managed by this service.</exception>
    /// <exception cref="SitecoreAuthException">Thrown when the configured authentication endpoint URL is invalid or insecure.</exception>
    /// <exception cref="SitecoreAuthHttpException">Thrown when the HTTP request fails or returns an error status code.</exception>
    /// <exception cref="SitecoreAuthResponseException">Thrown when the authentication response cannot be parsed or is invalid.</exception>
    public async Task<SitecoreAuthToken> TryRefreshSitecoreAuthToken(SitecoreAuthToken token, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            throw new TaskCanceledException(null, null, cancellationToken);
        }

        var credentials = _tokenCache.RemoveToken(token);
        if (!credentials.HasValue)
        {
            _logger?.LogWarning("Attempted to refresh a token not managed by the service.");
            throw new ArgumentException("The provided token is not managed by this service.", nameof(token));
        }
        
        return await GetSitecoreAuthToken(credentials.Value, cancellationToken).ConfigureAwait(false);
    }
}