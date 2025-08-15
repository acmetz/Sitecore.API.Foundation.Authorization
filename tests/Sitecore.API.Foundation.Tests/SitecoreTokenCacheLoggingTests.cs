using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.Models;
using Sitecore.API.Foundation.Authorization.Services;
using Xunit;

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
        var cache = CreateCache(out var logger);
        var creds = new SitecoreAuthClientCredentials("client-1", "secret");
        var token = new SitecoreAuthToken("t1", DateTimeOffset.UtcNow.AddMinutes(1));
        cache.SetToken(creds, token);

        // Act
        var result = cache.GetToken(creds);

        // Assert
        result.ShouldBe(token);
        logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Cache hit for clientId")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public void GetToken_WhenExpired_ShouldLogExpired()
    {
        // Arrange
        var cache = CreateCache(out var logger);
        var creds = new SitecoreAuthClientCredentials("client-2", "secret");
        var expired = new SitecoreAuthToken("e1", DateTimeOffset.UtcNow.AddSeconds(-5));
        cache.SetToken(creds, expired);

        // Act
        var result = cache.GetToken(creds);

        // Assert
        result.ShouldBeNull();
        logger.Received().Log(
            LogLevel.Debug,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Token expired for clientId")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public void PerformCleanup_WithExpiredTokens_ShouldLogRemovedCount()
    {
        // Arrange
        var cache = CreateCache(out var logger);
        var expired = new SitecoreAuthToken("expired", DateTimeOffset.UtcNow.AddSeconds(-1));
        var valid = new SitecoreAuthToken("valid", DateTimeOffset.UtcNow.AddMinutes(5));
        var c1 = new SitecoreAuthClientCredentials("c1", "s1");
        var c2 = new SitecoreAuthClientCredentials("c2", "s2");
        cache.SetToken(c1, expired);
        cache.SetToken(c2, valid);

        // Act
        cache.PerformCleanup();

        // Assert
        logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Cleanup removed")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public void SetToken_WhenExceedsMaxCacheSize_ShouldLogEviction()
    {
        // Arrange
        var options = new SitecoreTokenServiceOptions { MaxCacheSize = 1 };
        var cache = CreateCache(out var logger, options);
        var c1 = new SitecoreAuthClientCredentials("c1", "s1");
        var c2 = new SitecoreAuthClientCredentials("c2", "s2");
        var t1 = new SitecoreAuthToken("t1", DateTimeOffset.UtcNow.AddMinutes(1));
        var t2 = new SitecoreAuthToken("t2", DateTimeOffset.UtcNow.AddMinutes(2));

        cache.SetToken(c1, t1);

        // Act
        cache.SetToken(c2, t2);

        // Assert
        logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Evicted")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public void ClearCache_ShouldLogClearedCount()
    {
        // Arrange
        var cache = CreateCache(out var logger);
        var c1 = new SitecoreAuthClientCredentials("c1", "s1");
        var t1 = new SitecoreAuthToken("t1", DateTimeOffset.UtcNow.AddMinutes(1));
        cache.SetToken(c1, t1);

        // Act
        cache.ClearCache();

        // Assert
        logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Cache cleared")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public void RemoveToken_WhenFound_ShouldLogRemoval()
    {
        // Arrange
        var cache = CreateCache(out var logger);
        var c1 = new SitecoreAuthClientCredentials("c1", "s1");
        var t1 = new SitecoreAuthToken("t1", DateTimeOffset.UtcNow.AddMinutes(1));
        cache.SetToken(c1, t1);

        // Act
        cache.RemoveToken(t1);

        // Assert
        logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Removed token for clientId")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
