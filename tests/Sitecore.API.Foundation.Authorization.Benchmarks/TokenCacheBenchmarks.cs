using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.Options;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.Models;
using Sitecore.API.Foundation.Authorization.Services;

namespace Sitecore.API.Foundation.Authorization.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
[SimpleJob(RuntimeMoniker.Net10_0)]
public class TokenCacheBenchmarks : IDisposable
{
    private const int PopulatedCacheCount = 64;
    private const int EvictionRingMask = 127; // 128-entry ring (power of two) against MaxCacheSize = 64

    private SitecoreTokenCache? _populatedCache;
    private SitecoreTokenCache? _evictionCache;
    private SitecoreTokenCache? _removeCycleCache;

    private SitecoreAuthClientCredentials _hitCredentials;
    private SitecoreAuthClientCredentials _removeCredentials;
    private SitecoreAuthToken _primaryToken;
    private SitecoreAuthToken _updatedToken;

    private SitecoreAuthClientCredentials[] _evictionRingCredentials = Array.Empty<SitecoreAuthClientCredentials>();
    private DateTimeOffset _evictionBaseExpiration;
    private int _evictionIndex;
    private long _evictionTickOffset;

    [GlobalSetup]
    public void Setup()
    {
        var largeOptions = Options.Create(new SitecoreTokenServiceOptions
        {
            MaxCacheSize = 1000,
            CleanupThreshold = 1000,
            CleanupInterval = TimeSpan.FromHours(1)
        });

        _populatedCache = new SitecoreTokenCache(largeOptions);
        _removeCycleCache = new SitecoreTokenCache(largeOptions);

        var baseExpiration = DateTimeOffset.UtcNow.AddYears(1);
        for (var i = 0; i < PopulatedCacheCount; i++)
        {
            var creds = new SitecoreAuthClientCredentials($"client-{i}", $"secret-{i}");
            var token = new SitecoreAuthToken($"cached-token-{i}", baseExpiration.AddMinutes(i));
            _populatedCache.SetToken(creds, token);
            if (i < PopulatedCacheCount - 1)
            {
                _removeCycleCache.SetToken(creds, token);
            }
        }

        _hitCredentials = new SitecoreAuthClientCredentials("client-32", "secret-32");
        _removeCredentials = new SitecoreAuthClientCredentials("client-remove", "secret-remove");
        _primaryToken = new SitecoreAuthToken("cycle-token", baseExpiration);
        _updatedToken = new SitecoreAuthToken("updated-token", baseExpiration.AddHours(1));

        var evictionOptions = Options.Create(new SitecoreTokenServiceOptions
        {
            MaxCacheSize = PopulatedCacheCount,
            CleanupThreshold = 1000,
            CleanupInterval = TimeSpan.FromHours(1)
        });

        _evictionCache = new SitecoreTokenCache(evictionOptions);
        _evictionBaseExpiration = baseExpiration;
        _evictionRingCredentials = new SitecoreAuthClientCredentials[EvictionRingMask + 1];

        for (var i = 0; i <= EvictionRingMask; i++)
        {
            _evictionRingCredentials[i] = new SitecoreAuthClientCredentials($"evict-client-{i}", $"evict-secret-{i}");
        }

        for (var i = 0; i < PopulatedCacheCount; i++)
        {
            _evictionCache.SetToken(
                _evictionRingCredentials[i],
                new SitecoreAuthToken("evict-seed-token", _evictionBaseExpiration.AddTicks(i)));
        }

        _evictionIndex = PopulatedCacheCount - 1;
        _evictionTickOffset = PopulatedCacheCount;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();
    }

    [Benchmark(Description = "GetToken (Cache Hit)")]
    public SitecoreAuthToken? GetToken_Hit()
    {
        return _populatedCache!.GetToken(_hitCredentials);
    }

    [Benchmark(Description = "SetToken (Update Existing)")]
    public void SetToken_Update()
    {
        _populatedCache!.SetToken(_hitCredentials, _updatedToken);
    }

    [Benchmark(Description = "SetToken (MaxCacheSize Eviction)")]
    public void SetToken_EvictOldest()
    {
        _evictionIndex = (_evictionIndex + 1) & EvictionRingMask;
        var nextToken = new SitecoreAuthToken(
            "evict-token",
            _evictionBaseExpiration.AddTicks(++_evictionTickOffset));
        _evictionCache!.SetToken(_evictionRingCredentials[_evictionIndex], nextToken);
    }

    [Benchmark(Description = "RemoveToken (Set + Atomic Remove)")]
    public SitecoreAuthClientCredentials? RemoveToken_HitAndRestore()
    {
        _removeCycleCache!.SetToken(_removeCredentials, _primaryToken);
        return _removeCycleCache.RemoveToken(_primaryToken);
    }

    [Benchmark(Description = "PerformCleanup (64 Entries)")]
    public void PerformCleanup()
    {
        _populatedCache!.PerformCleanup();
    }

    public void Dispose()
    {
        _populatedCache?.Dispose();
        _evictionCache?.Dispose();
        _removeCycleCache?.Dispose();
        GC.SuppressFinalize(this);
    }
}
