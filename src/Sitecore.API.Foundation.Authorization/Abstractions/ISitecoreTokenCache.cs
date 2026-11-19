using System;
using Sitecore.API.Foundation.Authorization.Models;

namespace Sitecore.API.Foundation.Authorization.Abstractions;

/// <summary>
/// Interface for managing Sitecore authentication token cache operations.
/// </summary>
public interface ISitecoreTokenCache : IDisposable
{
    /// <summary>
    /// Gets the current number of tokens in the cache.
    /// </summary>
    int CacheSize { get; }

    /// <summary>
    /// Gets a cached token for the specified credentials if it exists and is not expired.
    /// </summary>
    /// <param name="credentials">The credentials to look up.</param>
    /// <returns>The cached token if found and valid, otherwise null.</returns>
    SitecoreAuthToken? GetToken(SitecoreAuthClientCredentials credentials);

    /// <summary>
    /// Gets a cached token for the specified credentials and authentication endpoint URL if it exists and is not expired.
    /// </summary>
    /// <param name="credentials">The credentials to look up.</param>
    /// <param name="authTokenUrl">The authentication token endpoint URL used to partition the cache.</param>
    /// <returns>The cached token if found and valid, otherwise null.</returns>
    SitecoreAuthToken? GetToken(SitecoreAuthClientCredentials credentials, string authTokenUrl) => GetToken(credentials);

    /// <summary>
    /// Stores a token in the cache for the specified credentials.
    /// </summary>
    /// <param name="credentials">The credentials associated with the token.</param>
    /// <param name="token">The token to cache.</param>
    void SetToken(SitecoreAuthClientCredentials credentials, SitecoreAuthToken token);

    /// <summary>
    /// Stores a token in the cache for the specified credentials and authentication endpoint URL.
    /// </summary>
    /// <param name="credentials">The credentials associated with the token.</param>
    /// <param name="token">The token to cache.</param>
    /// <param name="authTokenUrl">The authentication token endpoint URL used to partition the cache.</param>
    void SetToken(SitecoreAuthClientCredentials credentials, SitecoreAuthToken token, string authTokenUrl) => SetToken(credentials, token);

    /// <summary>
    /// Removes a token from the cache by finding the credentials associated with it.
    /// </summary>
    /// <param name="token">The token to remove.</param>
    /// <returns>The credentials that were associated with the token, or null if not found.</returns>
    SitecoreAuthClientCredentials? RemoveToken(SitecoreAuthToken token);

    /// <summary>
    /// Removes a token from the cache for the specified authentication endpoint URL by finding the credentials associated with it.
    /// </summary>
    /// <param name="token">The token to remove.</param>
    /// <param name="authTokenUrl">The authentication token endpoint URL that must match the cached entry.</param>
    /// <returns>The credentials that were associated with the token, or null if not found.</returns>
    SitecoreAuthClientCredentials? RemoveToken(SitecoreAuthToken token, string authTokenUrl) => RemoveToken(token);

    /// <summary>
    /// Clears all tokens from the cache.
    /// </summary>
    void ClearCache();

    /// <summary>
    /// Performs cleanup operations on the cache, removing expired tokens.
    /// </summary>
    void PerformCleanup();
}