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
    private readonly ILogger? _logger;
    private readonly bool _useLegacyCacheFallback;
    private volatile ValidatedAuthUrl? _validatedAuthUrl;

    private sealed record ValidatedAuthUrl(string RawUrl, Uri RequestUri);

    private readonly record struct AuthRequest(
        [property: JsonPropertyName("audience")] string Audience,
        [property: JsonPropertyName("grant_type")] string GrantType,
        [property: JsonPropertyName("client_id")] string ClientId,
        [property: JsonPropertyName("client_secret")] string ClientSecret);

    private readonly record struct AuthResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    /// <summary>
    /// Initializes a new instance of the <see cref="SitecoreTokenService"/> class.
    /// </summary>
    public SitecoreTokenService(
        HttpClient httpClient,
        ISitecoreTokenCache tokenCache,
        IOptions<SitecoreTokenServiceOptions> options,
        ILogger<SitecoreTokenService>? logger = null)
        : this(httpClient, tokenCache, options, (ILogger?)logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SitecoreTokenService"/> class with a non-generic <see cref="ILogger"/>.
    /// </summary>
    protected SitecoreTokenService(
        HttpClient httpClient,
        ISitecoreTokenCache tokenCache,
        IOptions<SitecoreTokenServiceOptions> options,
        ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(tokenCache);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Value, nameof(options));

        HttpClient = httpClient;
        _tokenCache = tokenCache;
        _options = options.Value;
        _logger = logger;
        _useLegacyCacheFallback = IsDynamicProxyCache(_tokenCache);
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
        ThrowIfCanceled(cancellationToken);

        if (string.IsNullOrEmpty(credentials.ClientId) || string.IsNullOrEmpty(credentials.ClientSecret))
        {
            throw new ArgumentException("Client credentials must specify non-empty ClientId and ClientSecret.", nameof(credentials));
        }

        var requestUri = ValidateAuthTokenUrl(credentials.ClientId);

        // Check for cached token
        var cachedToken = GetCachedToken(credentials);
        if (cachedToken.HasValue)
        {
            _logger?.LogInformation("Token cache hit for clientId {ClientId}.", credentials.ClientId);
            return cachedToken.Value;
        }

        _logger?.LogInformation("Requesting new token for clientId {ClientId} from {AuthUrl}.", credentials.ClientId, _options.AuthTokenUrl);
        _logger?.LogDebug("Auth request payload: audience={Audience}, grant_type={GrantType}, client_id={ClientId}.", Constants.SitecoreAuthAudience, Constants.SitecoreAuthGrantType, credentials.ClientId);

        var authRequest = new AuthRequest(
            Constants.SitecoreAuthAudience,
            Constants.SitecoreAuthGrantType,
            credentials.ClientId,
            credentials.ClientSecret);

        using var response = await HttpClient.PostAsJsonAsync(requestUri, authRequest, cancellationToken).ConfigureAwait(false);
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
            result = await response.Content.ReadFromJsonAsync<AuthResponse?>(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to parse authentication response for clientId {ClientId}.", credentials.ClientId);
            throw new SitecoreAuthResponseException("Failed to parse auth response.", ex);
        }

        if (result is not { } authResponse || string.IsNullOrWhiteSpace(authResponse.AccessToken))
        {
            _logger?.LogError("Authentication response was empty or missing access_token for clientId {ClientId}.", credentials.ClientId);
            throw new SitecoreAuthResponseException("Failed to read auth token from response or token is not set.");
        }

        if (authResponse.ExpiresIn <= 0)
        {
            _logger?.LogError("Auth response contained non-positive expires_in ({ExpiresIn}) for clientId {ClientId}.", authResponse.ExpiresIn, credentials.ClientId);
            throw new SitecoreAuthResponseException($"Auth response contained non-positive expires_in ({authResponse.ExpiresIn}).");
        }

        var goodUntil = DateTimeOffset.UtcNow.AddSeconds(authResponse.ExpiresIn);
        var sitecoreToken = new SitecoreAuthToken(authResponse.AccessToken, goodUntil);

        // Cache the new token
        CacheToken(credentials, sitecoreToken);
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
        ThrowIfCanceled(cancellationToken);
        ValidateAuthTokenUrl(clientId: null);

        var credentials = RemoveCachedToken(token);
        if (!credentials.HasValue)
        {
            _logger?.LogWarning("Attempted to refresh a token not managed by the service.");
            throw new ArgumentException("The provided token is not managed by this service.", nameof(token));
        }

        return await GetSitecoreAuthToken(credentials.Value, cancellationToken).ConfigureAwait(false);
    }

    private static void ThrowIfCanceled(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            throw new TaskCanceledException(null, null, cancellationToken);
        }
    }

    private Uri ValidateAuthTokenUrl(string? clientId)
    {
        var rawUrl = _options.AuthTokenUrl;
        if (_validatedAuthUrl is { } cached && ReferenceEquals(cached.RawUrl, rawUrl))
        {
            return cached.RequestUri;
        }

        if (string.IsNullOrWhiteSpace(rawUrl) ||
            !Uri.TryCreate(rawUrl, UriKind.Absolute, out var requestUri) ||
            !string.IsNullOrEmpty(requestUri.UserInfo) ||
            (requestUri.Scheme != Uri.UriSchemeHttps && !(requestUri.Scheme == Uri.UriSchemeHttp && requestUri.IsLoopback)))
        {
            _logger?.LogWarning(
                "Invalid or insecure AuthTokenUrl configured for clientId {ClientId}. AuthTokenUrl must be an absolute HTTPS URI (or loopback HTTP URI) without embedded credentials.",
                clientId ?? "unknown");
            throw new SitecoreAuthException("AuthTokenUrl must be an absolute HTTPS URI (or loopback HTTP URI) without embedded credentials.");
        }

        _validatedAuthUrl = new ValidatedAuthUrl(rawUrl, requestUri);
        return requestUri;
    }

    private SitecoreAuthToken? GetCachedToken(SitecoreAuthClientCredentials credentials)
        => _useLegacyCacheFallback
            ? (_tokenCache.GetToken(credentials, _options.AuthTokenUrl) ?? _tokenCache.GetToken(credentials))
            : _tokenCache.GetToken(credentials, _options.AuthTokenUrl);

    private void CacheToken(SitecoreAuthClientCredentials credentials, SitecoreAuthToken token)
    {
        _tokenCache.SetToken(credentials, token, _options.AuthTokenUrl);
        if (_useLegacyCacheFallback)
        {
            _tokenCache.SetToken(credentials, token);
        }
    }

    private SitecoreAuthClientCredentials? RemoveCachedToken(SitecoreAuthToken token)
        => _useLegacyCacheFallback
            ? (_tokenCache.RemoveToken(token, _options.AuthTokenUrl) ?? _tokenCache.RemoveToken(token))
            : _tokenCache.RemoveToken(token, _options.AuthTokenUrl);

    private static bool IsDynamicProxyCache(ISitecoreTokenCache tokenCache)
    {
        var type = tokenCache.GetType();
        if (type == typeof(SitecoreTokenCache))
        {
            return false;
        }

        return string.Equals(type.Namespace, "Castle.Proxies", StringComparison.Ordinal) ||
               (type.Assembly.FullName?.StartsWith("DynamicProxyGenAssembly2", StringComparison.Ordinal) ?? false) ||
               typeof(System.Reflection.DispatchProxy).IsAssignableFrom(type);
    }
}

/// <summary>
/// Typed service for creating and managing Sitecore authentication tokens for a specific consumer type <typeparamref name="TClient"/>.
/// </summary>
/// <typeparam name="TClient">The consumer type whose named <see cref="HttpClient"/> and options are used.</typeparam>
public class SitecoreTokenService<TClient> : SitecoreTokenService, ISitecoreTokenService<TClient>
    where TClient : class
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SitecoreTokenService{TClient}"/> class.
    /// </summary>
    public SitecoreTokenService(
        HttpClient httpClient,
        ISitecoreTokenCache tokenCache,
        IOptions<SitecoreTokenServiceOptions> options,
        ILogger<SitecoreTokenService<TClient>>? logger = null)
        : base(httpClient, tokenCache, options, (ILogger?)logger)
    {
    }
}