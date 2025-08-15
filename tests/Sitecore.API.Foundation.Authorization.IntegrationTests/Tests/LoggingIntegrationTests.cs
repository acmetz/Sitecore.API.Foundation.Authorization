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
public class LoggingIntegrationTests
{
    private readonly MockOAuth2TestFixture _mock;

    public LoggingIntegrationTests(MockOAuth2TestFixture mock) => _mock = mock;

    [Fact]
    public async Task Should_log_cache_hit_and_http_flow()
    {
        // Arrange
        await _mock.EnsureInitializedAsync();
        var options = Options.Create(new SitecoreTokenServiceOptions { AuthTokenUrl = _mock.TokenEndpoint });
        using var logger = new InMemoryLogger<SitecoreTokenService>();
        var cache = new SitecoreTokenCache(options);
        var svc = new SitecoreTokenService(_mock.HttpClient, cache, options, logger);
        var creds = new SitecoreAuthClientCredentials(_mock.ClientId, _mock.ClientSecret);

        // Act: first call -> network, second -> cache
        var t1 = await svc.GetSitecoreAuthToken(creds);
        var t2 = await svc.GetSitecoreAuthToken(creds);

        // Assert
        t1.AccessToken.ShouldNotBeEmpty();
        t2.ShouldBe(t1);

        logger.Entries.ShouldContain(e => e.Message.Contains("Requesting new token"));
        logger.Entries.ShouldContain(e => e.Message.Contains("Token acquired and cached"));
        logger.Entries.ShouldContain(e => e.Message.Contains("Token cache hit"));
    }

    [Fact]
    public async Task Should_log_error_when_response_invalid()
    {
        // Arrange: custom handler returning invalid json
        var handler = new MockOAuth2MessageHandler(_mock.ClientId, _mock.ClientSecret, _mock.TokenEndpoint);
        var http = new HttpClient(handler);
        var options = Options.Create(new SitecoreTokenServiceOptions { AuthTokenUrl = _mock.TokenEndpoint });
        using var logger = new InMemoryLogger<SitecoreTokenService>();
        var cache = new SitecoreTokenCache(options);
        var svc = new SitecoreTokenService(http, cache, options, logger);
        var creds = new SitecoreAuthClientCredentials(_mock.ClientId, _mock.ClientSecret);

        // Force invalid JSON by intercepting message content: we simulate by sending malformed token via direct call
        // Easiest: use a delegating handler that always returns 200 with invalid json
        var invalidHandler = new TestHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{ invalid json }")
        });
        var invalidHttp = new HttpClient(invalidHandler);
        var invalidSvc = new SitecoreTokenService(invalidHttp, cache, options, logger);

        // Act/Assert
        await Should.ThrowAsync<Exceptions.SitecoreAuthResponseException>(() => invalidSvc.GetSitecoreAuthToken(creds));
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Message.Contains("Failed to parse authentication response"));
    }

    [Fact]
    public async Task Should_log_service_and_cache_messages()
    {
        // Arrange
        await _mock.EnsureInitializedAsync();
        var options = Options.Create(new SitecoreTokenServiceOptions { AuthTokenUrl = _mock.TokenEndpoint });
        using var serviceLogger = new InMemoryLogger<SitecoreTokenService>();
        using var cacheLogger = new InMemoryLogger<SitecoreTokenCache>();
        var cache = new SitecoreTokenCache(options, cacheLogger);
        var svc = new SitecoreTokenService(_mock.HttpClient, cache, options, serviceLogger);
        var creds = new SitecoreAuthClientCredentials(_mock.ClientId, _mock.ClientSecret);

        // Act: first call (network), second (cache)
        var t1 = await svc.GetSitecoreAuthToken(creds);
        var t2 = await svc.GetSitecoreAuthToken(creds);

        // Assert: service logs
        serviceLogger.Entries.ShouldContain(e => e.Message.Contains("Requesting new token"));
        serviceLogger.Entries.ShouldContain(e => e.Message.Contains("Token acquired and cached"));
        serviceLogger.Entries.ShouldContain(e => e.Message.Contains("Token cache hit"));

        // Assert: cache logs
        cacheLogger.Entries.ShouldContain(e => e.Message.Contains("Cache miss for clientId"));
        cacheLogger.Entries.ShouldContain(e => e.Message.Contains("Token cached for clientId"));
        cacheLogger.Entries.ShouldContain(e => e.Message.Contains("Cache hit for clientId"));

        // Also validate returned tokens
        t1.AccessToken.ShouldNotBeEmpty();
        t2.ShouldBe(t1);
    }

    private sealed class TestHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public TestHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
