using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.Exceptions;
using Sitecore.API.Foundation.Authorization.Models;
using Sitecore.API.Foundation.Authorization.Services;
using Xunit;

namespace Sitecore.API.Foundation.Tests;

public class SitecoreTokenServiceLoggingTests
{
    private static SitecoreTokenService CreateService(
        ISitecoreTokenCache cache,
        HttpMessageHandler handler,
        ILogger<SitecoreTokenService>? logger = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };
        var options = Options.Create(new SitecoreTokenServiceOptions { AuthTokenUrl = "https://auth.test/token" });
        return new SitecoreTokenService(httpClient, cache, options, logger);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    [Fact]
    public async Task GetSitecoreAuthToken_UsesCacheAndLogsHit()
    {
        // Arrange
        var creds = new SitecoreAuthClientCredentials("id","secret");
        var token = new SitecoreAuthToken("tok", DateTimeOffset.UtcNow.AddMinutes(5));
        var cache = Substitute.For<ISitecoreTokenCache>();
        cache.GetToken(creds).Returns(token);
        var logger = Substitute.For<ILogger<SitecoreTokenService>>();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var svc = CreateService(cache, handler, logger);

        // Act
        var result = await svc.GetSitecoreAuthToken(creds);

        // Assert
        result.ShouldBe(token);
        logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Token cache hit")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task GetSitecoreAuthToken_HttpFailure_LogsWarningAndThrows()
    {
        // Arrange
        var creds = new SitecoreAuthClientCredentials("id","secret");
        var cache = Substitute.For<ISitecoreTokenCache>();
        cache.GetToken(creds).Returns((SitecoreAuthToken?)null);
        var logger = Substitute.For<ILogger<SitecoreTokenService>>();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var svc = CreateService(cache, handler, logger);

        // Act
        var act = Task.Run(() => svc.GetSitecoreAuthToken(creds));

        // Assert
        var ex = await Should.ThrowAsync<SitecoreAuthHttpException>(act);
        ex.StatusCode.ShouldBe((int)HttpStatusCode.BadRequest);
        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Authentication request failed")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task GetSitecoreAuthToken_ParseFailure_LogsErrorAndThrows()
    {
        // Arrange
        var creds = new SitecoreAuthClientCredentials("id","secret");
        var cache = Substitute.For<ISitecoreTokenCache>();
        cache.GetToken(creds).Returns((SitecoreAuthToken?)null);
        var logger = Substitute.For<ILogger<SitecoreTokenService>>();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ invalid json }")
        });
        var svc = CreateService(cache, handler, logger);

        // Act
        var act = Task.Run(() => svc.GetSitecoreAuthToken(creds));

        // Assert
        await Should.ThrowAsync<SitecoreAuthResponseException>(act);
        logger.Received().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Failed to parse authentication response")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
