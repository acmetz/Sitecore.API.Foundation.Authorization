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
public class TokenCacheBenchmarks
{
    private SitecoreTokenCache? _cache;
    private SitecoreAuthToken _token;
    private SitecoreAuthClientCredentials _credentials;
    private const string ClientId = "benchmark-client-id";

    [GlobalSetup]
    public void Setup()
    {
        var options = Options.Create(new SitecoreTokenServiceOptions
        {
            MaxCacheSize = 1000,
            CleanupThreshold = 1000,
            CleanupInterval = TimeSpan.FromMinutes(5)
        });

        _cache = new SitecoreTokenCache(options);
        _token = new SitecoreAuthToken("test-token", DateTimeOffset.UtcNow.AddHours(1));
        _credentials = new SitecoreAuthClientCredentials(ClientId, "test-secret");

        // Pre-populate for GetToken and ReplaceToken
        _cache.SetToken(_credentials, _token);
    }

    [IterationSetup(Target = nameof(AddToken))]
    public void IterationSetup()
    {
        // AddToken test should ideally happen on a cache without the exact same token to measure setting it
        // properly without it just hitting an overwrite fastpath, though the dictionary implementation makes it roughly the same
    }

    [Benchmark]
    public void AddToken()
    {
        _cache!.SetToken(_credentials, _token);
    }

    [Benchmark]
    public SitecoreAuthToken? GetToken()
    {
        return _cache!.GetToken(_credentials);
    }

    [Benchmark]
    public void ReplaceToken()
    {
        _cache!.SetToken(_credentials, _token);
        _cache!.SetToken(_credentials, _token);
    }
}
