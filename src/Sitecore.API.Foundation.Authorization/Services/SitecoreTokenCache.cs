using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.Models;

namespace Sitecore.API.Foundation.Authorization.Services;

/// <summary>
/// Thread-safe, in-memory token cache partitioned by credentials and authentication endpoint URL.
/// </summary>
public class SitecoreTokenCache : ISitecoreTokenCache
{
    private readonly record struct SitecoreTokenCacheKey(SitecoreAuthClientCredentials Credentials, string AuthTokenUrl);

    private static readonly IComparer<long> DescendingLongComparer = Comparer<long>.Create((a, b) => b.CompareTo(a));

    private readonly ConcurrentDictionary<SitecoreTokenCacheKey, SitecoreAuthToken> _tokens = new();
    private readonly SitecoreTokenServiceOptions _options;
    private readonly Lock _cleanupLock = new();
    private readonly ILogger<SitecoreTokenCache>? _logger;
    private long _lastCleanupTicks = DateTimeOffset.MinValue.Ticks;

    /// <summary>
    /// Initializes a new instance of the <see cref="SitecoreTokenCache"/> class.
    /// </summary>
    /// <param name="options">The token service configuration options.</param>
    /// <param name="logger">Optional logger for cache diagnostics.</param>
    public SitecoreTokenCache(IOptions<SitecoreTokenServiceOptions> options, ILogger<SitecoreTokenCache>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Value, nameof(options));
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public int CacheSize => _tokens.Count;

    private DateTimeOffset LastCleanup
    {
        get => new(Interlocked.Read(ref _lastCleanupTicks), TimeSpan.Zero);
        set => Interlocked.Exchange(ref _lastCleanupTicks, value.UtcTicks);
    }

    private string NormalizeAuthTokenUrl(string? authTokenUrl)
        => string.IsNullOrWhiteSpace(authTokenUrl)
            ? (_options.AuthTokenUrl?.Trim() ?? string.Empty)
            : authTokenUrl.Trim();

    /// <inheritdoc />
    public SitecoreAuthToken? GetToken(SitecoreAuthClientCredentials credentials)
        => GetToken(credentials, _options.AuthTokenUrl);

    /// <inheritdoc />
    public SitecoreAuthToken? GetToken(SitecoreAuthClientCredentials credentials, string authTokenUrl)
    {
        var key = new SitecoreTokenCacheKey(credentials, NormalizeAuthTokenUrl(authTokenUrl));

        // First attempt to retrieve and log the state (hit/miss/expired)
        var token = TryGetValidToken(key);
        if (token.HasValue) return token;

        // If cleanup is needed, perform it opportunistically
        if (ShouldPerformCleanup())
        {
            TryPerformCleanup();
        }

        return null;
    }

    private SitecoreAuthToken? TryGetValidToken(SitecoreTokenCacheKey key)
    {
        if (!_tokens.TryGetValue(key, out var token))
        {
            _logger?.LogDebug("Cache miss for clientId {ClientId}.", key.Credentials.ClientId);
            return null;
        }

        if (token.IsExpired)
        {
            _logger?.LogDebug("Token expired for clientId {ClientId}.", key.Credentials.ClientId);
            return null;
        }

        _logger?.LogInformation("Cache hit for clientId {ClientId}.", key.Credentials.ClientId);
        return token;
    }

    /// <inheritdoc />
    public void SetToken(SitecoreAuthClientCredentials credentials, SitecoreAuthToken token)
        => SetToken(credentials, token, _options.AuthTokenUrl);

    /// <inheritdoc />
    public void SetToken(SitecoreAuthClientCredentials credentials, SitecoreAuthToken token, string authTokenUrl)
    {
        var key = new SitecoreTokenCacheKey(credentials, NormalizeAuthTokenUrl(authTokenUrl));
        _tokens[key] = token;
        _logger?.LogInformation("Token cached for clientId {ClientId} until {Expiration:o}.", credentials.ClientId, token.Expiration);

        if (_tokens.Count > _options.MaxCacheSize)
        {
            TryEvictOldestTokens();
        }
    }

    /// <inheritdoc />
    public SitecoreAuthClientCredentials? RemoveToken(SitecoreAuthToken token)
        => RemoveTokenCore(token, normalizedAuthTokenUrl: null);

    /// <inheritdoc />
    public SitecoreAuthClientCredentials? RemoveToken(SitecoreAuthToken token, string authTokenUrl)
        => RemoveTokenCore(token, NormalizeAuthTokenUrl(authTokenUrl));

    private SitecoreAuthClientCredentials? RemoveTokenCore(SitecoreAuthToken token, string? normalizedAuthTokenUrl)
    {
        foreach (var kvp in _tokens)
        {
            if (kvp.Value.Equals(token) &&
                (normalizedAuthTokenUrl is null || string.Equals(kvp.Key.AuthTokenUrl, normalizedAuthTokenUrl, StringComparison.Ordinal)) &&
                _tokens.TryRemove(kvp))
            {
                _logger?.LogInformation("Removed token for clientId {ClientId} from cache.", kvp.Key.Credentials.ClientId);
                return kvp.Key.Credentials;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public void ClearCache()
    {
        int count;
        lock (_cleanupLock)
        {
            count = _tokens.Count;
            _tokens.Clear();
            LastCleanup = DateTimeOffset.MinValue;
        }
        _logger?.LogInformation("Cache cleared, removed {Count} token(s).", count);
    }

    /// <inheritdoc />
    public void PerformCleanup()
    {
        lock (_cleanupLock)
        {
            ExecuteCleanupUnsafe();
        }
    }

    private bool ShouldPerformCleanup()
    {
        return DateTimeOffset.UtcNow - LastCleanup > _options.CleanupInterval ||
               _tokens.Count > _options.CleanupThreshold ||
               HasExpiredTokens();
    }

    private bool HasExpiredTokens()
    {
        const int MaxSampleSize = 5;
        var count = 0;
        foreach (var kvp in _tokens)
        {
            if (kvp.Value.IsExpired) return true;
            if (++count >= MaxSampleSize) break;
        }
        return false;
    }

    private void TryExecuteWithCleanupLock(TimeSpan timeout, Action<SitecoreTokenCache> action)
    {
        if (!_cleanupLock.TryEnter(timeout)) return;
        try
        {
            action(this);
        }
        finally
        {
            _cleanupLock.Exit();
        }
    }

    private void TryPerformCleanup()
        => TryExecuteWithCleanupLock(TimeSpan.FromMilliseconds(10), static cache =>
        {
            if (!cache.ShouldPerformCleanup()) return;
            cache.ExecuteCleanupUnsafe();
        });

    private void ExecuteCleanupUnsafe()
    {
        var removed = CleanupExpiredTokensUnsafe();
        if (removed > 0)
        {
            _logger?.LogInformation("Cleanup removed {Count} expired token(s).", removed);
        }
        LastCleanup = DateTimeOffset.UtcNow;
    }

    private int CleanupExpiredTokensUnsafe()
    {
        var removed = 0;
        foreach (var kvp in _tokens)
        {
            if (kvp.Value.IsExpired && _tokens.TryRemove(kvp))
            {
                removed++;
            }
        }
        return removed;
    }

    private void TryEvictOldestTokens()
        => TryExecuteWithCleanupLock(TimeSpan.FromMilliseconds(50), static cache =>
        {
            var removed = cache.EvictOldestTokensUnsafe();
            if (removed > 0)
            {
                cache._logger?.LogInformation("Evicted {Count} token(s) due to cache size limit.", removed);
            }
        });

    private int EvictOldestTokensUnsafe()
    {
        var tokensToRemove = _tokens.Count - Math.Max(0, _options.MaxCacheSize);
        if (tokensToRemove <= 0) return 0;

        // Use a PriorityQueue as a max-heap (largest expiration time at the top)
        // We only keep 'tokensToRemove' elements in the queue, retaining the oldest (smallest expiration time) tokens.
        var pq = new PriorityQueue<KeyValuePair<SitecoreTokenCacheKey, SitecoreAuthToken>, long>(
            tokensToRemove,
            DescendingLongComparer);

        foreach (var kvp in _tokens)
        {
            var ticks = kvp.Value.Expiration.UtcTicks;
            if (pq.Count < tokensToRemove)
            {
                pq.Enqueue(kvp, ticks);
            }
            else
            {
                pq.TryPeek(out _, out var maxPriority);
                if (ticks < maxPriority)
                {
                    pq.DequeueEnqueue(kvp, ticks);
                }
            }
        }

        var removed = 0;
        while (pq.Count > 0)
        {
            if (_tokens.TryRemove(pq.Dequeue()))
            {
                removed++;
            }
        }
        return removed;
    }

    /// <summary>
    /// Releases unmanaged resources and optionally releases managed resources.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> if called from <see cref="Dispose()"/>; otherwise <see langword="false"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}