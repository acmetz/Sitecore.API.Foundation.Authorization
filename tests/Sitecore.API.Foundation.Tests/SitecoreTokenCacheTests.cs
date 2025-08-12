using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Shouldly;
using Sitecore.API.Foundation.Authorization;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.Models;
using Sitecore.API.Foundation.Authorization.Services;

namespace Sitecore.API.Foundation.Tests;

public class SitecoreTokenCacheTests : IDisposable
{
    private readonly SitecoreTokenCache _cache;
    private readonly SitecoreAuthClientCredentials _testCredentials;
    private readonly SitecoreAuthToken _testToken;

    public SitecoreTokenCacheTests()
    {
        var options = Options.Create(new SitecoreTokenServiceOptions());
        _cache = new SitecoreTokenCache(options);
        _testCredentials = new SitecoreAuthClientCredentials("test-client", "test-secret");
        _testToken = new SitecoreAuthToken("test-token", DateTimeOffset.UtcNow.AddHours(1));
    }

    public void Dispose()
    {
        _cache?.Dispose();
    }

    [Fact]
    public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Should.Throw<ArgumentNullException>(() => new SitecoreTokenCache(null!));
    }

    [Fact]
    public void CacheSize_WhenEmpty_ShouldReturnZero()
    {
        // Arrange & Act & Assert
        _cache.CacheSize.ShouldBe(0);
    }

    [Fact]
    public void GetToken_WhenNotCached_ShouldReturnNull()
    {
        // Arrange & Act
        var result = _cache.GetToken(_testCredentials);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void SetToken_ShouldIncreaseCacheSize()
    {
        // Arrange & Act
        _cache.SetToken(_testCredentials, _testToken);

        // Assert
        _cache.CacheSize.ShouldBe(1);
    }

    [Fact]
    public void GetToken_WithValidCachedToken_ShouldReturnToken()
    {
        // Arrange
        _cache.SetToken(_testCredentials, _testToken);

        // Act
        var result = _cache.GetToken(_testCredentials);

        // Assert
        result.ShouldBe(_testToken);
    }

    [Fact]
    public void GetToken_WithExpiredToken_ShouldReturnNull()
    {
        // Arrange
        var expiredToken = new SitecoreAuthToken("expired-token", DateTimeOffset.UtcNow.AddSeconds(-1));
        _cache.SetToken(_testCredentials, expiredToken);

        // Act
        var result = _cache.GetToken(_testCredentials);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void RemoveToken_WithCachedToken_ShouldReturnCredentials()
    {
        // Arrange
        _cache.SetToken(_testCredentials, _testToken);

        // Act
        var result = _cache.RemoveToken(_testToken);

        // Assert
        result.ShouldBe(_testCredentials);
        _cache.CacheSize.ShouldBe(0);
    }

    [Fact]
    public void RemoveToken_WithNonCachedToken_ShouldReturnNull()
    {
        // Arrange
        var otherToken = new SitecoreAuthToken("other-token", DateTimeOffset.UtcNow.AddHours(1));

        // Act
        var result = _cache.RemoveToken(otherToken);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void ClearCache_ShouldRemoveAllTokens()
    {
        // Arrange
        _cache.SetToken(_testCredentials, _testToken);
        var otherCredentials = new SitecoreAuthClientCredentials("other", "secret");
        var otherToken = new SitecoreAuthToken("other-token", DateTimeOffset.UtcNow.AddHours(1));
        _cache.SetToken(otherCredentials, otherToken);

        // Act
        _cache.ClearCache();

        // Assert
        _cache.CacheSize.ShouldBe(0);
    }

    [Fact]
    public void SetToken_ExceedingMaxCacheSize_ShouldEvictOldestTokens()
    {
        // Arrange
        var options = Options.Create(new SitecoreTokenServiceOptions { MaxCacheSize = 2 });
        var cache = new SitecoreTokenCache(options);

        var credentials1 = new SitecoreAuthClientCredentials("client1", "secret1");
        var credentials2 = new SitecoreAuthClientCredentials("client2", "secret2");
        var credentials3 = new SitecoreAuthClientCredentials("client3", "secret3");

        var token1 = new SitecoreAuthToken("token1", DateTimeOffset.UtcNow.AddHours(1));
        var token2 = new SitecoreAuthToken("token2", DateTimeOffset.UtcNow.AddHours(2));
        var token3 = new SitecoreAuthToken("token3", DateTimeOffset.UtcNow.AddHours(3));

        // Act
        cache.SetToken(credentials1, token1);
        cache.SetToken(credentials2, token2);
        cache.SetToken(credentials3, token3); // Should evict token1

        // Assert
        cache.CacheSize.ShouldBe(2);
        cache.GetToken(credentials1).ShouldBeNull(); // token1 should be evicted
        cache.GetToken(credentials2).ShouldBe(token2);
        cache.GetToken(credentials3).ShouldBe(token3);
    }

    [Fact]
    public void PerformCleanup_ShouldRemoveExpiredTokens()
    {
        // Arrange
        var expiredToken = new SitecoreAuthToken("expired-token", DateTimeOffset.UtcNow.AddSeconds(-1));
        var validToken = new SitecoreAuthToken("valid-token", DateTimeOffset.UtcNow.AddHours(1));
        
        var expiredCredentials = new SitecoreAuthClientCredentials("expired", "secret");
        var validCredentials = new SitecoreAuthClientCredentials("valid", "secret");

        _cache.SetToken(expiredCredentials, expiredToken);
        _cache.SetToken(validCredentials, validToken);

        // Act
        _cache.PerformCleanup();

        // Assert
        _cache.CacheSize.ShouldBe(1);
        _cache.GetToken(expiredCredentials).ShouldBeNull();
        _cache.GetToken(validCredentials).ShouldBe(validToken);
    }

    [Fact]
    public async Task ConcurrentOperations_ShouldHandleThreadSafetyCorrectly()
    {
        // Arrange
        var options = Options.Create(new SitecoreTokenServiceOptions { MaxCacheSize = 50 });
        var cache = new SitecoreTokenCache(options);
        var credentialsList = new List<SitecoreAuthClientCredentials>();
        var tokensList = new List<SitecoreAuthToken>();

        for (int i = 0; i < 20; i++)
        {
            credentialsList.Add(new SitecoreAuthClientCredentials($"client-{i}", $"secret-{i}"));
            tokensList.Add(new SitecoreAuthToken($"token-{i}", DateTimeOffset.UtcNow.AddHours(1)));
        }

        // Act - Concurrent operations from multiple threads
        var tasks = new List<Task>();

        // Add tokens concurrently
        for (int i = 0; i < 20; i++)
        {
            var index = i; // Capture for closure
            tasks.Add(Task.Run(() => cache.SetToken(credentialsList[index], tokensList[index])));
        }

        // Get tokens concurrently
        for (int i = 0; i < 20; i++)
        {
            var index = i; // Capture for closure
            tasks.Add(Task.Run(() => 
            {
                var token = cache.GetToken(credentialsList[index]);
                return token;
            }));
        }

        // Remove some tokens concurrently
        for (int i = 0; i < 5; i++)
        {
            var index = i; // Capture for closure
            tasks.Add(Task.Run(() => cache.RemoveToken(tokensList[index])));
        }

        // Wait for all operations to complete
        await Task.WhenAll(tasks);

        // Assert
        cache.CacheSize.ShouldBeLessThanOrEqualTo(20);
        cache.CacheSize.ShouldBeGreaterThanOrEqualTo(15); // Some were removed
    }

    [Fact]
    public async Task ConcurrentCleanupOperations_ShouldNotCauseDeadlocks()
    {
        // Arrange
        var options = Options.Create(new SitecoreTokenServiceOptions 
        { 
            MaxCacheSize = 10,
            CleanupThreshold = 15,
            CleanupInterval = TimeSpan.FromMilliseconds(100)
        });
        var cache = new SitecoreTokenCache(options);

        // Act - Concurrent cleanup operations
        var tasks = new List<Task>();

        // Add expired tokens that will trigger cleanup
        for (int i = 0; i < 20; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                var credentials = new SitecoreAuthClientCredentials($"cleanup-client-{index}", $"cleanup-secret-{index}");
                var token = new SitecoreAuthToken($"cleanup-token-{index}", DateTimeOffset.UtcNow.AddSeconds(-1)); // Expired
                cache.SetToken(credentials, token);
            }));
        }

        // Trigger manual cleanups
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() => cache.PerformCleanup()));
        }

        // Get operations that might trigger cleanup
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                var credentials = new SitecoreAuthClientCredentials($"get-client-{index}", $"get-secret-{index}");
                cache.GetToken(credentials);
            }));
        }

        // Assert - Should complete without deadlocks
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await Task.WhenAll(tasks).WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new Exception("Operations did not complete within timeout - possible deadlock detected");
        }
    }

    [Fact]
    public async Task GetToken_ConcurrentReadAccess_ShouldAllowMultipleReaders()
    {
        // Arrange
        var cache = new SitecoreTokenCache(Options.Create(new SitecoreTokenServiceOptions()));
        cache.SetToken(_testCredentials, _testToken);

        // Act - Multiple concurrent readers
        var readTasks = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => cache.GetToken(_testCredentials)))
            .ToArray();

        var results = await Task.WhenAll(readTasks);

        // Assert
        results.ShouldAllBe(token => token == _testToken);
        results.Length.ShouldBe(50);
    }

    [Fact]
    public void HighVolumeOperations_ShouldPerformEfficiently()
    {
        // Arrange
        var options = Options.Create(new SitecoreTokenServiceOptions { MaxCacheSize = 1000 });
        var cache = new SitecoreTokenCache(options);
        var stopwatch = Stopwatch.StartNew();

        // Act - Perform many operations
        for (int i = 0; i < 500; i++)
        {
            var credentials = new SitecoreAuthClientCredentials($"perf-client-{i}", $"perf-secret-{i}");
            var token = new SitecoreAuthToken($"perf-token-{i}", DateTimeOffset.UtcNow.AddHours(1));
            
            cache.SetToken(credentials, token);
            cache.GetToken(credentials);
        }

        stopwatch.Stop();

        // Assert - Should complete within reasonable time (adjust as needed)
        stopwatch.ElapsedMilliseconds.ShouldBeLessThan(2000); // Reduced from 5 seconds to 2 seconds for better performance
        cache.CacheSize.ShouldBeGreaterThan(0);
        
        // Clean up
        cache.Dispose();
    }

    [Fact]
    public async Task ConcurrentDictionaryPerformance_ShouldBeOptimal()
    {
        // Arrange
        var options = Options.Create(new SitecoreTokenServiceOptions { MaxCacheSize = 100 });
        var cache = new SitecoreTokenCache(options);
        var stopwatch = Stopwatch.StartNew();

        // Act - Concurrent read/write operations
        var tasks = new List<Task>();

        // Add writer tasks
        for (int i = 0; i < 10; i++)
        {
            var writerIndex = i;
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 10; j++)
                {
                    var credentials = new SitecoreAuthClientCredentials($"writer-{writerIndex}-{j}", $"secret-{writerIndex}-{j}");
                    var token = new SitecoreAuthToken($"token-{writerIndex}-{j}", DateTimeOffset.UtcNow.AddHours(1));
                    cache.SetToken(credentials, token);
                }
            }));
        }

        // Add reader tasks
        for (int i = 0; i < 20; i++)
        {
            var readerIndex = i;
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 5; j++)
                {
                    var credentials = new SitecoreAuthClientCredentials($"reader-{readerIndex}-{j}", $"secret-{readerIndex}-{j}");
                    cache.GetToken(credentials); // Will return null but tests concurrent access
                }
            }));
        }

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert - Should handle concurrent operations efficiently
        stopwatch.ElapsedMilliseconds.ShouldBeLessThan(3000); // Should complete quickly
        cache.CacheSize.ShouldBeGreaterThan(0);
        
        // Clean up
        cache.Dispose();
    }
}