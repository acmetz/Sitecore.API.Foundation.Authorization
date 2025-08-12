using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Options;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.Models;

namespace Sitecore.API.Foundation.Authorization.Services;

/// <summary>
/// High-performance, thread-safe in-memory cache for Sitecore authentication tokens using ConcurrentDictionary.
/// </summary>
public class SitecoreTokenCache : ISitecoreTokenCache
{
    private readonly ConcurrentDictionary<SitecoreAuthClientCredentials, SitecoreAuthToken> _tokens = new();
    private readonly SitecoreTokenServiceOptions _options;
    private readonly ReaderWriterLockSlim _cleanupLock = new();
    private long _lastCleanupTicks = DateTimeOffset.MinValue.Ticks;

    /// <summary>
    /// Initializes a new instance of the <see cref="SitecoreTokenCache"/> class.
    /// </summary>
    /// <param name="options">The configuration options for the token cache.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public SitecoreTokenCache(IOptions<SitecoreTokenServiceOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Gets the current number of tokens in the cache.
    /// </summary>
    public int CacheSize => _tokens.Count;

    private DateTimeOffset LastCleanup 
    { 
        get => new DateTimeOffset(Interlocked.Read(ref _lastCleanupTicks), TimeSpan.Zero);
        set => Interlocked.Exchange(ref _lastCleanupTicks, value.Ticks);
    }

    /// <summary>
    /// Gets a cached token for the specified credentials if it exists and is not expired.
    /// </summary>
    /// <param name="credentials">The credentials to look up.</param>
    /// <returns>The cached token if found and valid, otherwise null.</returns>
    public SitecoreAuthToken? GetToken(SitecoreAuthClientCredentials credentials)
    {
        if (!ShouldPerformCleanup())
        {
            return TryGetValidToken(credentials);
        }
        TryPerformCleanup();
        return TryGetValidToken(credentials);

    }

    private SitecoreAuthToken? TryGetValidToken(SitecoreAuthClientCredentials credentials)
    {
        if (_tokens.TryGetValue(credentials, out var token) && !token.IsExpired)
        {
            return token;
        }
        return null;
    }

    /// <summary>
    /// Stores a token in the cache for the specified credentials.
    /// </summary>
    /// <param name="credentials">The credentials associated with the token.</param>
    /// <param name="token">The token to cache.</param>
    public void SetToken(SitecoreAuthClientCredentials credentials, SitecoreAuthToken token)
    {
        _tokens.AddOrUpdate(credentials, token, (_, _) => token);

        // Check if cache size exceeds limit and evict if necessary
        if (_tokens.Count > _options.MaxCacheSize)
        {
            TryEvictOldestTokens();
        }
    }

    /// <summary>
    /// Removes a token from the cache by finding the credentials associated with it.
    /// </summary>
    /// <param name="token">The token to remove.</param>
    /// <returns>The credentials that were associated with the token, or null if not found.</returns>
    public SitecoreAuthClientCredentials? RemoveToken(SitecoreAuthToken token)
    {
        // Find the first matching key and remove it
        var keyToRemove = _tokens.FirstOrDefault(kvp => kvp.Value.Equals(token)).Key;
        bool found = !EqualityComparer<SitecoreAuthClientCredentials>.Default.Equals(keyToRemove, default);
        if (found && _tokens.TryRemove(keyToRemove, out _))
        {
            return keyToRemove;
        }
        return null;
    }

    /// <summary>
    /// Clears all tokens from the cache.
    /// </summary>
    public void ClearCache()
    {
        _tokens.Clear();
        LastCleanup = DateTimeOffset.MinValue;
    }

    /// <summary>
    /// Performs cleanup operations on the cache, removing expired tokens and enforcing size limits.
    /// </summary>
    public void PerformCleanup()
    {
        _cleanupLock.EnterWriteLock();
        try
        {
            CleanupExpiredTokensUnsafe();
            LastCleanup = DateTimeOffset.UtcNow;
        }
        finally
        {
            _cleanupLock.ExitWriteLock();
        }
    }

    private bool ShouldPerformCleanup()
    {
        return _tokens.Count > _options.CleanupThreshold ||
               DateTimeOffset.UtcNow - LastCleanup > _options.CleanupInterval ||
               HasExpiredTokens();
    }

    private bool HasExpiredTokens()
    {
        // Quick check for expired tokens (sampling approach for performance)
        int sampleSize = Math.Min(5, _tokens.Count);
        int count = 0;
        foreach (var kvp in _tokens)
        {
            if (kvp.Value.IsExpired) return true;
            count++;
            if (count >= sampleSize) break;
        }
        return false;
    }

    private void TryPerformCleanup()
    {
        // Use TryEnterWriteLock to avoid blocking read operations
        if (_cleanupLock.TryEnterWriteLock(TimeSpan.FromMilliseconds(10)))
        {
            try
            {
                // Double-check if cleanup is still needed
                if (ShouldPerformCleanup())
                {
                    CleanupExpiredTokensUnsafe();
                    LastCleanup = DateTimeOffset.UtcNow;
                }
            }
            finally
            {
                _cleanupLock.ExitWriteLock();
            }
        }
        // If we can't get the lock quickly, let another thread handle cleanup
    }

    private void CleanupExpiredTokensUnsafe()
    {
        foreach (var key in _tokens.Where(kvp => kvp.Value.IsExpired).Select(kvp => kvp.Key).ToList())
        {
            _tokens.TryRemove(key, out _);
        }
    }

    private void TryEvictOldestTokens()
    {
        if (!_cleanupLock.TryEnterWriteLock(TimeSpan.FromMilliseconds(50)))
            return;
        try
        {
            EvictOldestTokensUnsafe();
        }
        finally
        {
            _cleanupLock.ExitWriteLock();
        }
    }

    private void EvictOldestTokensUnsafe()
    {
        var tokensToRemove = _tokens.Count - _options.MaxCacheSize;
        if (tokensToRemove <= 0 || _tokens.Count == 0) return;
        var keysToRemove = _tokens
            .OrderBy(kvp => kvp.Value.Expiration)
            .Take(Math.Min(tokensToRemove, _tokens.Count))
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var key in keysToRemove)
        {
            _tokens.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="SitecoreTokenCache"/> and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cleanupLock?.Dispose();
        }
    }

    /// <summary>
    /// Releases all resources used by the <see cref="SitecoreTokenCache"/>.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}