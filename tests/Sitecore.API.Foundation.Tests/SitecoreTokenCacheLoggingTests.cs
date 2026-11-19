using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.Models;
using Sitecore.API.Foundation.Authorization.Services;

namespace Sitecore.API.Foundation.Tests;

public class SitecoreTokenCacheLoggingTests
{
    private static SitecoreTokenCache CreateCache(out ILogger<SitecoreTokenCache> logger, SitecoreTokenServiceOptions? options = null)
    {
        logger = Substitute.For<ILogger<SitecoreTokenCache>>();
        var opts = Options.Create(options ?? new SitecoreTokenServiceOptions());
        return new SitecoreTokenCache(opts, logger);
    }

    [Fact]
    public void GetToken_WhenCached_ShouldLogCacheHit()
    {
        // Arrange
        using var cache = CreateCache(out var logger);
        var creds = new SitecoreAuthClientCredentials("client-1", "secret");
        var token = new SitecoreAuthToken("t1", DateTimeOffset.UtcNow.AddMinutes(1));
        cache.SetToken(creds, token);

        // Act
        var result = cache.GetToken(creds);

        // Assert
        result.ShouldBe(token);
        logger.ShouldHaveLogged(LogLevel.Information, "Cache hit for clientId");
    }

    [Fact]
    public void GetToken_WhenExpired_ShouldLogExpired()
    {
        // Arrange
        using var cache = CreateCache(out var logger);
        var creds = new SitecoreAuthClientCredentials("client-2", "secret");
        var expired = new SitecoreAuthToken("e1", DateTimeOffset.UtcNow.AddSeconds(-5));
        cache.SetToken(creds, expired);

        // Act
        var result = cache.GetToken(creds);

        // Assert
        result.ShouldBeNull();
        logger.ShouldHaveLogged(LogLevel.Debug, "Token expired for clientId");
    }

    [Fact]
    public void PerformCleanup_WithExpiredTokens_ShouldLogRemovedCount()
    {
        // Arrange
        using var cache = CreateCache(out var logger);
        var expired = new SitecoreAuthToken("expired", DateTimeOffset.UtcNow.AddSeconds(-1));
        var valid = new SitecoreAuthToken("valid", DateTimeOffset.UtcNow.AddMinutes(5));
        var c1 = new SitecoreAuthClientCredentials("c1", "s1");
        var c2 = new SitecoreAuthClientCredentials("c2", "s2");
        cache.SetToken(c1, expired);
        cache.SetToken(c2, valid);

        // Act
        cache.PerformCleanup();

        // Assert
        logger.ShouldHaveLogged(LogLevel.Information, "Cleanup removed 1 expired token(s).");
    }

    [Fact]
    public void SetToken_WhenExceedsMaxCacheSize_ShouldLogEviction()
    {
        // Arrange
        var options = new SitecoreTokenServiceOptions { MaxCacheSize = 1 };
        using var cache = CreateCache(out var logger, options);
        var c1 = new SitecoreAuthClientCredentials("c1", "s1");
        var c2 = new SitecoreAuthClientCredentials("c2", "s2");
        var t1 = new SitecoreAuthToken("t1", DateTimeOffset.UtcNow.AddMinutes(1));
        var t2 = new SitecoreAuthToken("t2", DateTimeOffset.UtcNow.AddMinutes(2));

        cache.SetToken(c1, t1);

        // Act
        cache.SetToken(c2, t2);

        // Assert
        logger.ShouldHaveLogged(LogLevel.Information, "Evicted 1 token(s) due to cache size limit.");
    }

    [Fact]
    public void ClearCache_ShouldLogClearedCount()
    {
        // Arrange
        using var cache = CreateCache(out var logger);
        var c1 = new SitecoreAuthClientCredentials("c1", "s1");
        var t1 = new SitecoreAuthToken("t1", DateTimeOffset.UtcNow.AddMinutes(1));
        cache.SetToken(c1, t1);

        // Act
        cache.ClearCache();

        // Assert
        logger.ShouldHaveLogged(LogLevel.Information, "Cache cleared, removed 1 token(s).");
    }

    [Fact]
    public void RemoveToken_WhenFound_ShouldLogRemoval()
    {
        // Arrange
        using var cache = CreateCache(out var logger);
        var c1 = new SitecoreAuthClientCredentials("c1", "s1");
        var t1 = new SitecoreAuthToken("t1", DateTimeOffset.UtcNow.AddMinutes(1));
        cache.SetToken(c1, t1);

        // Act
        cache.RemoveToken(t1);

        // Assert
        logger.ShouldHaveLogged(LogLevel.Information, "Removed token for clientId c1 from cache.");
    }

    [Fact]
    public void GetToken_SetToken_AndNoOpCleanupOrRemove_ShouldLogExpectedMessagesWithoutLeakingSecrets()
    {
        // Arrange
        using var cache = CreateCache(out var logger);
        var creds = new SitecoreAuthClientCredentials("log-client", "super-secret-client-pass");
        var token = new SitecoreAuthToken("super-secret-bearer-jwt", DateTimeOffset.UtcNow.AddMinutes(5));
        var otherToken = new SitecoreAuthToken("other", DateTimeOffset.UtcNow.AddMinutes(5));

        // 1. Cache miss logs Debug
        cache.GetToken(creds).ShouldBeNull();
        logger.ShouldHaveLogged(LogLevel.Debug, "Cache miss for clientId log-client.");

        // 2. SetToken logs Info without leaking secret/token
        cache.SetToken(creds, token);
        logger.ShouldHaveLogged(LogLevel.Information, "Token cached for clientId log-client");

        // 3. PerformCleanup with 0 expired tokens does NOT log "Cleanup removed"
        cache.PerformCleanup();
        logger.ShouldNotHaveLogged("Cleanup removed");

        // 4. RemoveToken miss does NOT log "Removed token"
        cache.RemoveToken(otherToken).ShouldBeNull();
        logger.ShouldNotHaveLogged("Removed token");

        // 5. Never log client secrets or raw access tokens
        logger.ShouldNotHaveLogged("super-secret-client-pass");
        logger.ShouldNotHaveLogged("super-secret-bearer-jwt");
    }
}
