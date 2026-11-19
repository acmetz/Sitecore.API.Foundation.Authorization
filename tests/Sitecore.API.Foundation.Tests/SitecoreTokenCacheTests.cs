using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
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
    public void Constructor_WithNullOptions_OrNullOptionsValue_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Should.Throw<ArgumentNullException>(() => new SitecoreTokenCache(null!))
            .ParamName.ShouldBe("options");

        var nullValueOptions = Substitute.For<IOptions<SitecoreTokenServiceOptions>>();
        nullValueOptions.Value.Returns((SitecoreTokenServiceOptions)null!);
        Should.Throw<ArgumentNullException>(() => new SitecoreTokenCache(nullValueOptions))
            .ParamName.ShouldBe("options");
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
        _cache.CacheSize.ShouldBe(0);
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
        _cache.CacheSize.ShouldBe(0);
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
        _cache.CacheSize.ShouldBe(0);
    }

    [Fact]
    public void RemoveToken_WhenCredentialsUpdatedToNewToken_ShouldNotRemoveNewToken()
    {
        // Arrange
        var oldToken = new SitecoreAuthToken("old-token", DateTimeOffset.UtcNow.AddHours(1));
        var newToken = new SitecoreAuthToken("new-token", DateTimeOffset.UtcNow.AddHours(2));
        _cache.SetToken(_testCredentials, oldToken);
        _cache.SetToken(_testCredentials, newToken);

        // Act
        var removedCredentials = _cache.RemoveToken(oldToken);

        // Assert
        removedCredentials.ShouldBeNull();
        _cache.GetToken(_testCredentials).ShouldBe(newToken);
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
        using var cache = new SitecoreTokenCache(options);

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
        using var cache = new SitecoreTokenCache(options);
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
        using var cache = new SitecoreTokenCache(options);

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
        await Task.WhenAll(tasks).WaitAsync(cts.Token);
    }

    [Fact]
    public async Task GetToken_ConcurrentReadAccess_ShouldAllowMultipleReaders()
    {
        // Arrange
        using var cache = new SitecoreTokenCache(Options.Create(new SitecoreTokenServiceOptions()));
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
    public void HighVolumeOperations_ShouldRetainAllEntriesWhenUnderMaxCacheSize()
    {
        // Arrange
        var options = Options.Create(new SitecoreTokenServiceOptions { MaxCacheSize = 1000 });
        using var cache = new SitecoreTokenCache(options);

        // Act - Perform many operations
        for (int i = 0; i < 500; i++)
        {
            var credentials = new SitecoreAuthClientCredentials($"perf-client-{i}", $"perf-secret-{i}");
            var token = new SitecoreAuthToken($"perf-token-{i}", DateTimeOffset.UtcNow.AddHours(1));
            
            cache.SetToken(credentials, token);
            cache.GetToken(credentials).ShouldBe(token);
        }

        // Assert
        cache.CacheSize.ShouldBe(500);
    }

    [Fact]
    public async Task ConcurrentDictionaryPerformance_ShouldRetainAllConcurrentWrites()
    {
        // Arrange
        var options = Options.Create(new SitecoreTokenServiceOptions { MaxCacheSize = 100 });
        using var cache = new SitecoreTokenCache(options);

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

        // Assert
        cache.CacheSize.ShouldBe(100);
    }

    [Fact]
    public void EndpointPartitionedOverloads_ShouldIsolateTokensByAuthTokenUrl()
    {
        // Arrange
        var urlA = "https://auth-a.sitecorecloud.io/oauth/token";
        var urlB = "https://auth-b.sitecorecloud.io/oauth/token";
        var tokenA = new SitecoreAuthToken("token-a", DateTimeOffset.UtcNow.AddHours(1));
        var tokenB = new SitecoreAuthToken("token-b", DateTimeOffset.UtcNow.AddHours(1));

        // Act
        _cache.SetToken(_testCredentials, tokenA, urlA);
        _cache.SetToken(_testCredentials, tokenB, urlB);

        // Assert
        _cache.CacheSize.ShouldBe(2);
        _cache.GetToken(_testCredentials, urlA).ShouldBe(tokenA);
        _cache.GetToken(_testCredentials, urlB).ShouldBe(tokenB);

        // Removing tokenA against urlB should fail and leave tokenA intact
        _cache.RemoveToken(tokenA, urlB).ShouldBeNull();
        _cache.GetToken(_testCredentials, urlA).ShouldBe(tokenA);

        // Removing tokenA against urlA should succeed and return credentials
        _cache.RemoveToken(tokenA, urlA).ShouldBe(_testCredentials);
        _cache.GetToken(_testCredentials, urlA).ShouldBeNull();
        _cache.GetToken(_testCredentials, urlB).ShouldBe(tokenB);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EndpointPartitionedOverloads_WithNullOrWhitespaceUrl_ShouldFallbackToDefaultAuthTokenUrl(string? whitespaceOrNullUrl)
    {
        // Arrange
        _cache.SetToken(_testCredentials, _testToken, whitespaceOrNullUrl!);

        // Act & Assert - Should match the 1-arg GetToken and RemoveToken using default AuthTokenUrl
        _cache.GetToken(_testCredentials).ShouldBe(_testToken);
        _cache.GetToken(_testCredentials, whitespaceOrNullUrl!).ShouldBe(_testToken);
        _cache.RemoveToken(_testToken, whitespaceOrNullUrl!).ShouldBe(_testCredentials);
        _cache.CacheSize.ShouldBe(0);
    }

    [Fact]
    public void EndpointPartitionedOverloads_WhenDefaultAuthTokenUrlHasWhitespace_ShouldNormalizeConsistently()
    {
        // Arrange
        using var cache = new SitecoreTokenCache(Options.Create(new SitecoreTokenServiceOptions
        {
            AuthTokenUrl = "  https://auth.sitecorecloud.io/oauth/token  "
        }));

        // Act
        cache.SetToken(_testCredentials, _testToken);

        // Assert - Both 1-arg and null/whitespace 2-arg lookups must resolve the same trimmed key
        cache.GetToken(_testCredentials).ShouldBe(_testToken);
        cache.GetToken(_testCredentials, null!).ShouldBe(_testToken);
        cache.GetToken(_testCredentials, "   ").ShouldBe(_testToken);
        cache.RemoveToken(_testToken, "   ").ShouldBe(_testCredentials);
        cache.CacheSize.ShouldBe(0);
    }

    [Fact]
    public void EndpointPartitionedOverloads_WhenNonDefaultAuthTokenUrlHasWhitespace_ShouldNormalizeConsistently()
    {
        // Arrange
        const string canonicalUrl = "https://staging-auth.sitecorecloud.io/oauth/token";
        const string paddedSetUrl = "  https://staging-auth.sitecorecloud.io/oauth/token\t ";
        const string paddedGetUrl = "\thttps://staging-auth.sitecorecloud.io/oauth/token   ";
        const string paddedRemoveUrl = "   https://staging-auth.sitecorecloud.io/oauth/token ";

        // Act
        _cache.SetToken(_testCredentials, _testToken, paddedSetUrl);

        // Assert - Lookups and removals with canonical or differently-padded URLs resolve the same partition
        _cache.CacheSize.ShouldBe(1);
        _cache.GetToken(_testCredentials, canonicalUrl).ShouldBe(_testToken);
        _cache.GetToken(_testCredentials, paddedGetUrl).ShouldBe(_testToken);
        _cache.RemoveToken(_testToken, paddedRemoveUrl).ShouldBe(_testCredentials);
        _cache.CacheSize.ShouldBe(0);
    }

    [Fact]
    public void EndpointPartitionedOverloads_CleanupAndEviction_ShouldOperatePerCompositeKeyForSameCredentials()
    {
        // Arrange
        var options = Options.Create(new SitecoreTokenServiceOptions { MaxCacheSize = 2 });
        using var cache = new SitecoreTokenCache(options);

        const string urlExpired = "https://expired-auth.sitecorecloud.io/oauth/token";
        const string urlOldestValid = "https://oldest-auth.sitecorecloud.io/oauth/token";
        const string urlMiddleValid = "https://middle-auth.sitecorecloud.io/oauth/token";
        const string urlNewestValid = "https://newest-auth.sitecorecloud.io/oauth/token";

        var expiredToken = new SitecoreAuthToken("tok-expired", DateTimeOffset.UtcNow.AddSeconds(-5));
        var oldestValidToken = new SitecoreAuthToken("tok-oldest", DateTimeOffset.UtcNow.AddMinutes(10));
        var middleValidToken = new SitecoreAuthToken("tok-middle", DateTimeOffset.UtcNow.AddMinutes(20));
        var newestValidToken = new SitecoreAuthToken("tok-newest", DateTimeOffset.UtcNow.AddMinutes(30));

        // Act 1: Store an expired entry and a valid entry for the SAME credentials across two endpoints, then cleanup
        cache.SetToken(_testCredentials, expiredToken, urlExpired);
        cache.SetToken(_testCredentials, oldestValidToken, urlOldestValid);
        cache.CacheSize.ShouldBe(2);

        cache.PerformCleanup();

        // Assert 1: Only the expired endpoint partition is removed; valid partition for same credentials remains
        cache.CacheSize.ShouldBe(1);
        cache.GetToken(_testCredentials, urlExpired).ShouldBeNull();
        cache.GetToken(_testCredentials, urlOldestValid).ShouldBe(oldestValidToken);

        // Act 2: Add two more partitions for the SAME credentials, exceeding MaxCacheSize = 2
        cache.SetToken(_testCredentials, middleValidToken, urlMiddleValid);
        cache.SetToken(_testCredentials, newestValidToken, urlNewestValid);

        // Assert 2: Oldest valid partition for the same credentials is evicted; two newest partitions remain
        cache.CacheSize.ShouldBe(2);
        cache.GetToken(_testCredentials, urlOldestValid).ShouldBeNull();
        cache.GetToken(_testCredentials, urlMiddleValid).ShouldBe(middleValidToken);
        cache.GetToken(_testCredentials, urlNewestValid).ShouldBe(newestValidToken);
    }

    [Fact]
    public void CustomISitecoreTokenCache_DefaultInterfaceMethods_ShouldForwardToOneAndTwoArgMethods()
    {
        // Arrange
        using ISitecoreTokenCache customCache = new LegacyCustomTokenCache();

        // Act
        customCache.SetToken(_testCredentials, _testToken, "https://custom.sitecorecloud.io/oauth/token");
        var fetched = customCache.GetToken(_testCredentials, "https://custom.sitecorecloud.io/oauth/token");
        var removed = customCache.RemoveToken(_testToken, "https://custom.sitecorecloud.io/oauth/token");

        // Assert
        fetched.ShouldBe(_testToken);
        removed.ShouldBe(_testCredentials);
        customCache.CacheSize.ShouldBe(0);
    }

    [Fact]
    public async Task CustomISitecoreTokenCache_WhenUsedWithSitecoreTokenService_ShouldInvokeUnderlyingMethodsOncePerOperation()
    {
        // Arrange
        using var customCache = new LegacyCustomTokenCache();
        using var handler = new DelegateHttpMessageHandler((_, attempt) =>
            TestHttpResponseHelper.CreateTokenResponse($"custom-cache-token-{attempt}"));
        using var httpClient = new HttpClient(handler);
        var options = Options.Create(new SitecoreTokenServiceOptions
        {
            AuthTokenUrl = "https://auth.sitecorecloud.io/oauth/token"
        });
        var service = new SitecoreTokenService(httpClient, customCache, options);

        // Act 1: Cache miss -> 1 GetToken + 1 SetToken
        var token1 = await service.GetSitecoreAuthToken(_testCredentials);
        token1.AccessToken.ShouldBe("custom-cache-token-1");
        customCache.GetTokenCallCount.ShouldBe(1);
        customCache.SetTokenCallCount.ShouldBe(1);
        customCache.RemoveTokenCallCount.ShouldBe(0);

        // Act 2: Cache hit -> 1 GetToken, no additional SetToken
        var token2 = await service.GetSitecoreAuthToken(_testCredentials);
        token2.ShouldBe(token1);
        customCache.GetTokenCallCount.ShouldBe(2);
        customCache.SetTokenCallCount.ShouldBe(1);

        // Act 3: Refresh hit -> 1 RemoveToken + 1 GetToken (miss) + 1 SetToken
        var refreshedToken = await service.TryRefreshSitecoreAuthToken(token1);
        refreshedToken.AccessToken.ShouldBe("custom-cache-token-2");
        customCache.RemoveTokenCallCount.ShouldBe(1);
        customCache.GetTokenCallCount.ShouldBe(3);
        customCache.SetTokenCallCount.ShouldBe(2);

        // Act 4: Refresh miss (unmanaged token) -> 1 RemoveToken, throws ArgumentException
        var unmanagedToken = new SitecoreAuthToken("unmanaged-token", DateTimeOffset.UtcNow.AddHours(1));
        await Should.ThrowAsync<ArgumentException>(() => service.TryRefreshSitecoreAuthToken(unmanagedToken));
        customCache.RemoveTokenCallCount.ShouldBe(2);
    }

    [Fact]
    public void NormalizeAuthTokenUrl_WithNullDefaultUrlOrdinalCasingAndTrailingSlash_ShouldPartitionCorrectly()
    {
        // 1. Default AuthTokenUrl = null falls back to string.Empty
        using var nullDefaultCache = new SitecoreTokenCache(Options.Create(new SitecoreTokenServiceOptions
        {
            AuthTokenUrl = null!
        }));
        nullDefaultCache.SetToken(_testCredentials, _testToken);
        nullDefaultCache.GetToken(_testCredentials).ShouldBe(_testToken);
        nullDefaultCache.GetToken(_testCredentials, "").ShouldBe(_testToken);
        nullDefaultCache.GetToken(_testCredentials, "https://other.example/oauth/token").ShouldBeNull();

        // 2. Ordinal URL comparison treats differing casing or trailing slash as distinct partitions
        const string lowerUrl = "https://auth.sitecorecloud.io/oauth/token";
        const string upperHostUrl = "https://AUTH.sitecorecloud.io/oauth/token";
        const string trailingSlashUrl = "https://auth.sitecorecloud.io/oauth/token/";

        var tokLower = new SitecoreAuthToken("tok-lower", DateTimeOffset.UtcNow.AddHours(1));
        var tokUpper = new SitecoreAuthToken("tok-upper", DateTimeOffset.UtcNow.AddHours(1));
        var tokSlash = new SitecoreAuthToken("tok-slash", DateTimeOffset.UtcNow.AddHours(1));

        _cache.SetToken(_testCredentials, tokLower, lowerUrl);
        _cache.SetToken(_testCredentials, tokUpper, upperHostUrl);
        _cache.SetToken(_testCredentials, tokSlash, trailingSlashUrl);

        _cache.CacheSize.ShouldBe(3);
        _cache.GetToken(_testCredentials, lowerUrl).ShouldBe(tokLower);
        _cache.GetToken(_testCredentials, upperHostUrl).ShouldBe(tokUpper);
        _cache.GetToken(_testCredentials, trailingSlashUrl).ShouldBe(tokSlash);
    }

    [Fact]
    public void MaxCacheSize_BoundaryAndEvictionOrdering_ShouldHandleUpdatesZeroNegativeAndMixedExpirations()
    {
        // 1. Updating an existing key at MaxCacheSize must NOT evict another entry
        using var atCapacityCache = new SitecoreTokenCache(Options.Create(new SitecoreTokenServiceOptions { MaxCacheSize = 2 }));
        var c1 = new SitecoreAuthClientCredentials("c1", "s1");
        var c2 = new SitecoreAuthClientCredentials("c2", "s2");
        var t1 = new SitecoreAuthToken("t1", DateTimeOffset.UtcNow.AddMinutes(5));
        var t2 = new SitecoreAuthToken("t2", DateTimeOffset.UtcNow.AddMinutes(10));
        var t2Updated = new SitecoreAuthToken("t2-updated", DateTimeOffset.UtcNow.AddMinutes(20));

        atCapacityCache.SetToken(c1, t1);
        atCapacityCache.SetToken(c2, t2);
        atCapacityCache.SetToken(c2, t2Updated);

        atCapacityCache.CacheSize.ShouldBe(2);
        atCapacityCache.GetToken(c1).ShouldBe(t1);
        atCapacityCache.GetToken(c2).ShouldBe(t2Updated);

        // 2. MaxCacheSize = 1 retains latest expiration, even when inserting an earlier-expiring entry (evicts self)
        using var sizeOneCache = new SitecoreTokenCache(Options.Create(new SitecoreTokenServiceOptions { MaxCacheSize = 1 }));
        var tLong = new SitecoreAuthToken("long", DateTimeOffset.UtcNow.AddHours(2));
        var tMedium = new SitecoreAuthToken("medium", DateTimeOffset.UtcNow.AddHours(1));
        var tShort = new SitecoreAuthToken("short", DateTimeOffset.UtcNow.AddMinutes(30));

        sizeOneCache.SetToken(c1, tMedium);
        sizeOneCache.SetToken(c2, tLong);
        sizeOneCache.CacheSize.ShouldBe(1);
        sizeOneCache.GetToken(c1).ShouldBeNull();
        sizeOneCache.GetToken(c2).ShouldBe(tLong);

        sizeOneCache.SetToken(new SitecoreAuthClientCredentials("c3", "s3"), tShort);
        sizeOneCache.CacheSize.ShouldBe(1);
        sizeOneCache.GetToken(c2).ShouldBe(tLong);

        // 3. Out-of-order expirations with mixed UTC offsets exercise PriorityQueue.DequeueEnqueue on UtcTicks
        using var mixedOrderCache = new SitecoreTokenCache(Options.Create(new SitecoreTokenServiceOptions { MaxCacheSize = 2 }));
        var baseUtc = DateTimeOffset.UtcNow;
        // True UTC instants: earliest (+30m), second (+60m), third (+90m), fourth (+120m) inserted out of order
        var tokThird = new SitecoreAuthToken("third", baseUtc.AddMinutes(90).ToOffset(TimeSpan.FromHours(5)));
        var tokEarliest = new SitecoreAuthToken("earliest", baseUtc.AddMinutes(30).ToOffset(TimeSpan.FromHours(-5)));
        var tokFourth = new SitecoreAuthToken("fourth", baseUtc.AddMinutes(120));
        var tokSecond = new SitecoreAuthToken("second", baseUtc.AddMinutes(60).ToOffset(TimeSpan.FromHours(2)));

        mixedOrderCache.SetToken(new SitecoreAuthClientCredentials("m3", "s"), tokThird);
        mixedOrderCache.SetToken(new SitecoreAuthClientCredentials("m1", "s"), tokEarliest);
        mixedOrderCache.SetToken(new SitecoreAuthClientCredentials("m4", "s"), tokFourth);
        mixedOrderCache.SetToken(new SitecoreAuthClientCredentials("m2", "s"), tokSecond);

        mixedOrderCache.CacheSize.ShouldBe(2);
        mixedOrderCache.GetToken(new SitecoreAuthClientCredentials("m1", "s")).ShouldBeNull();
        mixedOrderCache.GetToken(new SitecoreAuthClientCredentials("m2", "s")).ShouldBeNull();
        mixedOrderCache.GetToken(new SitecoreAuthClientCredentials("m3", "s")).ShouldBe(tokThird);
        mixedOrderCache.GetToken(new SitecoreAuthClientCredentials("m4", "s")).ShouldBe(tokFourth);

        // 4. MaxCacheSize = 0 and int.MinValue (no signed overflow) evict immediately to 0
        using var zeroCache = new SitecoreTokenCache(Options.Create(new SitecoreTokenServiceOptions { MaxCacheSize = 0 }));
        zeroCache.SetToken(c1, tMedium);
        zeroCache.CacheSize.ShouldBe(0);

        using var negativeCache = new SitecoreTokenCache(Options.Create(new SitecoreTokenServiceOptions { MaxCacheSize = int.MinValue }));
        negativeCache.SetToken(c1, tMedium);
        negativeCache.CacheSize.ShouldBe(0);
    }

    [Fact]
    public void RemoveToken_WithDuplicateTokenAcrossEndpoints_ShouldRemoveOnlyTargetOrOnePerCall()
    {
        const string urlA = "https://dup-a.sitecorecloud.io/oauth/token";
        const string urlB = "https://dup-b.sitecorecloud.io/oauth/token";

        _cache.SetToken(_testCredentials, _testToken, urlA);
        _cache.SetToken(_testCredentials, _testToken, urlB);
        _cache.CacheSize.ShouldBe(2);

        // 2-arg removal removes only urlA
        _cache.RemoveToken(_testToken, urlA).ShouldBe(_testCredentials);
        _cache.CacheSize.ShouldBe(1);
        _cache.GetToken(_testCredentials, urlA).ShouldBeNull();
        _cache.GetToken(_testCredentials, urlB).ShouldBe(_testToken);

        // Re-add both and verify 1-arg RemoveToken removes one entry per call
        _cache.SetToken(_testCredentials, _testToken, urlA);
        _cache.CacheSize.ShouldBe(2);
        _cache.RemoveToken(_testToken).ShouldBe(_testCredentials);
        _cache.CacheSize.ShouldBe(1);
        _cache.RemoveToken(_testToken).ShouldBe(_testCredentials);
        _cache.CacheSize.ShouldBe(0);
    }

    [Fact]
    public void CleanupTriggers_IntervalThresholdAndClearCacheReset_ShouldOperateIndependently()
    {
        var logger = Substitute.For<ILogger<SitecoreTokenCache>>();
        var options = Options.Create(new SitecoreTokenServiceOptions
        {
            MaxCacheSize = 50,
            CleanupThreshold = 2,
            CleanupInterval = TimeSpan.FromMinutes(10)
        });
        using var cache = new SitecoreTokenCache(options, logger);

        // Establish initial LastCleanup = UtcNow
        cache.PerformCleanup();

        // Empty / all-valid PerformCleanup leaves CacheSize untouched and does not log "Cleanup removed"
        var validCreds1 = new SitecoreAuthClientCredentials("v1", "s");
        var validCreds2 = new SitecoreAuthClientCredentials("v2", "s");
        cache.SetToken(validCreds1, new SitecoreAuthToken("vt1", DateTimeOffset.UtcNow.AddHours(1)));
        cache.SetToken(validCreds2, new SitecoreAuthToken("vt2", DateTimeOffset.UtcNow.AddHours(1)));
        cache.PerformCleanup();
        cache.CacheSize.ShouldBe(2);
        logger.ShouldNotHaveLogged("Cleanup removed");

        // Trigger CleanupThreshold (> 2 entries) so expired entries at tail or on miss are cleaned
        var expiredCreds = new SitecoreAuthClientCredentials("exp", "s");
        cache.SetToken(expiredCreds, new SitecoreAuthToken("et", DateTimeOffset.UtcNow.AddSeconds(-1)));
        cache.CacheSize.ShouldBe(3);

        // Cache miss triggers ShouldPerformCleanup because Count (3) > CleanupThreshold (2)
        cache.GetToken(new SitecoreAuthClientCredentials("miss", "s")).ShouldBeNull();
        cache.CacheSize.ShouldBe(2);
        cache.GetToken(expiredCreds).ShouldBeNull();

        // ClearCache resets LastCleanup to MinValue so immediate subsequent miss runs cleanup
        cache.ClearCache();
        cache.CacheSize.ShouldBe(0);

        // Interval trigger: short interval elapses naturally without threshold or sample hits
        using var intervalCache = new SitecoreTokenCache(Options.Create(new SitecoreTokenServiceOptions
        {
            MaxCacheSize = 50,
            CleanupThreshold = 100,
            CleanupInterval = TimeSpan.FromMilliseconds(20)
        }));
        intervalCache.PerformCleanup();
        for (int i = 0; i < 6; i++)
        {
            intervalCache.SetToken(
                new SitecoreAuthClientCredentials($"iv-{i}", "s"),
                new SitecoreAuthToken($"t-{i}", DateTimeOffset.UtcNow.AddMinutes(5)));
        }

        // Even before interval, double Dispose is idempotent
        intervalCache.Dispose();
        intervalCache.Dispose();
    }

    [Fact]
    public async Task ConcurrentClearCache_WithSetAndCleanup_ShouldRemainConsistentAndDeadlockFree()
    {
        using var cache = new SitecoreTokenCache(Options.Create(new SitecoreTokenServiceOptions { MaxCacheSize = 5 }));
        var tasks = new List<Task>();
        for (int i = 0; i < 15; i++)
        {
            var idx = i;
            tasks.Add(Task.Run(() => cache.SetToken(
                new SitecoreAuthClientCredentials($"clr-{idx}", "s"),
                new SitecoreAuthToken($"tok-{idx}", DateTimeOffset.UtcNow.AddMinutes(idx + 1)))));
            if (idx % 3 == 0)
            {
                tasks.Add(Task.Run(() => cache.ClearCache()));
                tasks.Add(Task.Run(() => cache.PerformCleanup()));
            }
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Task.WhenAll(tasks).WaitAsync(cts.Token);
        cache.CacheSize.ShouldBeLessThanOrEqualTo(5);
    }

    private sealed class LegacyCustomTokenCache : ISitecoreTokenCache
    {
        private readonly Dictionary<SitecoreAuthClientCredentials, SitecoreAuthToken> _store = new();

        public int CacheSize => _store.Count;
        public int GetTokenCallCount { get; private set; }
        public int SetTokenCallCount { get; private set; }
        public int RemoveTokenCallCount { get; private set; }

        public SitecoreAuthToken? GetToken(SitecoreAuthClientCredentials credentials)
        {
            GetTokenCallCount++;
            return _store.TryGetValue(credentials, out var token) ? token : null;
        }

        public void SetToken(SitecoreAuthClientCredentials credentials, SitecoreAuthToken token)
        {
            SetTokenCallCount++;
            _store[credentials] = token;
        }

        public SitecoreAuthClientCredentials? RemoveToken(SitecoreAuthToken token)
        {
            RemoveTokenCallCount++;
            foreach (var kvp in _store)
            {
                if (kvp.Value.Equals(token))
                {
                    _store.Remove(kvp.Key);
                    return kvp.Key;
                }
            }

            return null;
        }

        public void ClearCache() => _store.Clear();

        public void PerformCleanup()
        {
        }

        public void Dispose()
        {
        }
    }
}