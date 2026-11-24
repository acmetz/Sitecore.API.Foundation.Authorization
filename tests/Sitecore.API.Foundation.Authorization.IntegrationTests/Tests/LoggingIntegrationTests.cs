using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.IntegrationTests.Fixtures;
using Sitecore.API.Foundation.Authorization.Models;
using Sitecore.API.Foundation.Authorization.Services;
using Xunit;

namespace Sitecore.API.Foundation.Authorization.IntegrationTests.Tests;

[Collection("Mock OAuth2 Collection")]
public class LoggingIntegrationTests(MockOAuth2TestFixture mock)
{
    [Fact]
    public async Task Should_log_error_when_response_invalid()
    {
        // Arrange: custom handler returning 200 with invalid json
        var options = Options.Create(new SitecoreTokenServiceOptions { AuthTokenUrl = mock.TokenEndpoint });
        using var logger = new InMemoryLogger<SitecoreTokenService>();
        using var cache = new SitecoreTokenCache(options);
        var creds = new SitecoreAuthClientCredentials(mock.ClientId, mock.ClientSecret);

        using var invalidHandler = new TestHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{ invalid json }")
        });
        using var invalidHttp = new HttpClient(invalidHandler);
        var invalidSvc = new SitecoreTokenService(invalidHttp, cache, options, logger);

        // Act/Assert
        await Should.ThrowAsync<Exceptions.SitecoreAuthResponseException>(() => invalidSvc.GetSitecoreAuthToken(creds));
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Message.Contains("Failed to parse authentication response"));
        logger.Entries.ShouldNotContain(e => e.Message.Contains(mock.ClientSecret));
    }

    [Fact]
    public async Task Should_log_service_and_cache_messages()
    {
        // Arrange
        await mock.EnsureInitializedAsync();
        var options = Options.Create(new SitecoreTokenServiceOptions { AuthTokenUrl = mock.TokenEndpoint });
        using var serviceLogger = new InMemoryLogger<SitecoreTokenService>();
        using var cacheLogger = new InMemoryLogger<SitecoreTokenCache>();
        using var cache = new SitecoreTokenCache(options, cacheLogger);
        var svc = new SitecoreTokenService(mock.HttpClient, cache, options, serviceLogger);
        var creds = new SitecoreAuthClientCredentials(mock.ClientId, mock.ClientSecret);

        // Act: first call (network), second (cache)
        var t1 = await svc.GetSitecoreAuthToken(creds);
        var t2 = await svc.GetSitecoreAuthToken(creds);

        // Assert: service logs (with exact levels and no secret/token leakage)
        serviceLogger.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("Requesting new token"));
        serviceLogger.Entries.ShouldContain(e => e.Level == LogLevel.Debug && e.Message.Contains("Auth request payload:"));
        serviceLogger.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("Token acquired and cached"));
        serviceLogger.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("Token cache hit"));
        serviceLogger.Entries.ShouldNotContain(e => e.Message.Contains(mock.ClientSecret));
        serviceLogger.Entries.ShouldNotContain(e => e.Message.Contains(t1.AccessToken));

        // Assert: cache logs (with exact levels and no secret/token leakage)
        cacheLogger.Entries.ShouldContain(e => e.Level == LogLevel.Debug && e.Message.Contains("Cache miss for clientId"));
        cacheLogger.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("Token cached for clientId"));
        cacheLogger.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("Cache hit for clientId"));
        cacheLogger.Entries.ShouldNotContain(e => e.Message.Contains(mock.ClientSecret));
        cacheLogger.Entries.ShouldNotContain(e => e.Message.Contains(t1.AccessToken));

        // Also validate returned tokens
        t1.AccessToken.ShouldNotBeEmpty();
        t2.ShouldBe(t1);
    }

    private sealed class TestHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
