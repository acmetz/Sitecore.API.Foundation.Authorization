using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
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

    private class AuthResponse
    {
        public string? access_token { get; set; }
        public int expires_in { get; set; }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SitecoreTokenService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for authentication requests.</param>
    /// <param name="tokenCache">The token cache to use for storing and retrieving tokens.</param>
    /// <param name="options">The configuration options for the token service.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="httpClient"/>, <paramref name="tokenCache"/>, or <paramref name="options"/> is null.</exception>
    public SitecoreTokenService(HttpClient httpClient, ISitecoreTokenCache tokenCache, IOptions<SitecoreTokenServiceOptions> options) 
    {
        HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _tokenCache = tokenCache ?? throw new ArgumentNullException(nameof(tokenCache));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Gets a Sitecore authentication token for the specified credentials.
    /// Tokens are cached and reused until they expire. Automatic cleanup of expired tokens is performed periodically.
    /// </summary>
    /// <param name="credentials">The client credentials to authenticate with.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the authentication token.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="credentials"/> is null.</exception>
    /// <exception cref="SitecoreAuthHttpException">Thrown when the HTTP request fails or returns an error status code.</exception>
    /// <exception cref="SitecoreAuthResponseException">Thrown when the authentication response cannot be parsed or is invalid.</exception>
    public async Task<SitecoreAuthToken> GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials)
    {
        // Check for cached token
        var cachedToken = _tokenCache.GetToken(credentials);
        if (cachedToken.HasValue)
        {
            return cachedToken.Value;
        }
        
        var authRequest = new
        {
            audience = Constants.SitecoreAuthAudience,
            grant_type = Constants.SitecoreAuthGrantType,
            client_id = credentials.ClientId,
            client_secret = credentials.ClientSecret,
        };
        
        using HttpResponseMessage response = await HttpClient.PostAsJsonAsync(_options.AuthTokenUrl, authRequest);

        if (!response.IsSuccessStatusCode)
        {
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
            throw new SitecoreAuthResponseException("Failed to parse auth response.", ex);
        }
        
        if (result is null || string.IsNullOrEmpty(result.access_token))
        {
            throw new SitecoreAuthResponseException("Failed to read auth token from response or token is not set.");
        }
        
        var goodUntil = DateTimeOffset.UtcNow.AddSeconds(result.expires_in);
        var sitecoreToken = new SitecoreAuthToken(result.access_token, goodUntil);
        
        // Cache the new token
        _tokenCache.SetToken(credentials, sitecoreToken);
        
        return sitecoreToken;
    }

    /// <summary>
    /// Refreshes an existing Sitecore authentication token by re-authenticating with the same credentials.
    /// The old token is removed from the cache and a new token is generated.
    /// </summary>
    /// <param name="token">The token to refresh.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the new authentication token.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="token"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the token is not managed by this service.</exception>
    /// <exception cref="SitecoreAuthHttpException">Thrown when the HTTP request fails or returns an error status code.</exception>
    /// <exception cref="SitecoreAuthResponseException">Thrown when the authentication response cannot be parsed or is invalid.</exception>
    public async Task<SitecoreAuthToken> TryRefreshSitecoreAuthToken(SitecoreAuthToken token)
    {
        var credentials = _tokenCache.RemoveToken(token);
        if (!credentials.HasValue)
        {
            throw new ArgumentException("The provided token is not managed by this service.", nameof(token));
        }
        
        return await GetSitecoreAuthToken(credentials.Value);
    }
}