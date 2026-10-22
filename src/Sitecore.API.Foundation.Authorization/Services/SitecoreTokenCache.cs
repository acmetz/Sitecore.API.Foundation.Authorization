using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.Models;

namespace Sitecore.API.Foundation.Authorization.Services;

public class SitecoreTokenCache : ISitecoreTokenCache
{
    private readonly ConcurrentDictionary<SitecoreAuthClientCredentials, SitecoreAuthToken> _tokens = new();
    private readonly SitecoreTokenServiceOptions _options;
    private readonly ReaderWriterLockSlim _cleanupLock = new();
    private readonly ILogger<SitecoreTokenCache>? _logger;
    private long _lastCleanupTicks = DateTimeOffset.MinValue.Ticks;

    public SitecoreTokenCache(IOptions<SitecoreTokenServiceOptions> options, ILogger<SitecoreTokenCache>? logger = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    public int CacheSize => _tokens.Count;

    private DateTimeOffset LastCleanup 
    { 
        get => new DateTimeOffset(Interlocked.Read(ref _lastCleanupTicks), TimeSpan.Zero);
        set => Interlocked.Exchange(ref _lastCleanupTicks, value.Ticks);
    }

    public SitecoreAuthToken? GetToken(SitecoreAuthClientCredentials credentials)
    {
        // First attempt to retrieve and log the state (hit/miss/expired)
        var token = TryGetValidToken(credentials);
        if (token.HasValue) return token;

        // If cleanup is needed, perform it opportunistically
        if (ShouldPerformCleanup())
        {
            TryPerformCleanup();
        }

        return null;
    }

    private SitecoreAuthToken? TryGetValidToken(SitecoreAuthClientCredentials credentials)
    {
        if (!_tokens.TryGetValue(credentials, out var token))
        {
            _logger?.LogDebug("Cache miss for clientId {ClientId}.", credentials.ClientId);
            return null;
        }

        if (token.IsExpired)
        {
            _logger?.LogDebug("Token expired for clientId {ClientId}.", credentials.ClientId);
            return null;
        }

        _logger?.LogInformation("Cache hit for clientId {ClientId}.", credentials.ClientId);
        return token;
    }

    public void SetToken(SitecoreAuthClientCredentials credentials, SitecoreAuthToken token)
    {
        _tokens.AddOrUpdate(credentials, token, (_, _) => token);
        _logger?.LogInformation("Token cached for clientId {ClientId} until {Expiration:o}.", credentials.ClientId, token.Expiration);

        if (_tokens.Count > _options.MaxCacheSize)
        {
            TryEvictOldestTokens();
        }
    }

    public SitecoreAuthClientCredentials? RemoveToken(SitecoreAuthToken token)
    {
        var keyToRemove = _tokens.FirstOrDefault(kvp => kvp.Value.Equals(token)).Key;
        bool found = !EqualityComparer<SitecoreAuthClientCredentials>.Default.Equals(keyToRemove, default);
        if (!found) return null;

        if (_tokens.TryRemove(keyToRemove, out _))
        {
            _logger?.LogInformation("Removed token for clientId {ClientId} from cache.", keyToRemove.ClientId);
            return keyToRemove;
        }

        return null;
    }

    public void ClearCache()
    {
        var count = _tokens.Count;
        _tokens.Clear();
        LastCleanup = DateTimeOffset.MinValue;
        _logger?.LogInformation("Cache cleared, removed {Count} token(s).", count);
    }

    public void PerformCleanup()
    {
        _cleanupLock.EnterWriteLock();
        try
        {
            var before = _tokens.Count;
            CleanupExpiredTokensUnsafe();
            var removed = before - _tokens.Count;
            if (removed > 0)
            {
                _logger?.LogInformation("Cleanup removed {Count} expired token(s).", removed);
            }
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
        if (!_cleanupLock.TryEnterWriteLock(TimeSpan.FromMilliseconds(10))) return;
        try
        {
            if (!ShouldPerformCleanup()) return;

            var before = _tokens.Count;
            CleanupExpiredTokensUnsafe();
            var removed = before - _tokens.Count;
            if (removed > 0)
            {
                _logger?.LogInformation("Cleanup removed {Count} expired token(s).", removed);
            }
            LastCleanup = DateTimeOffset.UtcNow;
        }
        finally
        {
            _cleanupLock.ExitWriteLock();
        }
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
        if (!_cleanupLock.TryEnterWriteLock(TimeSpan.FromMilliseconds(50))) return;
        try
        {
            var before = _tokens.Count;
            EvictOldestTokensUnsafe();
            var removed = before - _tokens.Count;
            if (removed > 0)
            {
                _logger?.LogInformation("Evicted {Count} token(s) due to cache size limit.", removed);
            }
        }
        finally
        {
            _cleanupLock.ExitWriteLock();
        }
    }

    private void EvictOldestTokensUnsafe()
    {
        var tokensToRemove = Math.Min(_tokens.Count - _options.MaxCacheSize, _tokens.Count);
        if (tokensToRemove <= 0) return;

        // Use a PriorityQueue as a max-heap (largest expiration time at the top)
        // We only keep 'tokensToRemove' elements in the queue, retaining the oldest (smallest expiration time) tokens.
        var pq = new PriorityQueue<SitecoreAuthClientCredentials, long>(
            tokensToRemove,
            Comparer<long>.Create((a, b) => b.CompareTo(a)));

        foreach (var kvp in _tokens)
        {
            var ticks = kvp.Value.Expiration.UtcTicks;
            if (pq.Count < tokensToRemove)
            {
                pq.Enqueue(kvp.Key, ticks);
            }
            else
            {
                pq.TryPeek(out _, out var maxPriority);
                if (ticks < maxPriority)
                {
                    pq.DequeueEnqueue(kvp.Key, ticks);
                }
            }
        }

        while (pq.Count > 0)
        {
            _tokens.TryRemove(pq.Dequeue(), out _);
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cleanupLock?.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}