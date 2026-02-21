using System;
using System.Net;
using System.Net.Http;
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
using Sitecore.API.Foundation.Tests.Mocks;
using Xunit;
using Xunit.Abstractions;

namespace Sitecore.API.Foundation.Tests;

public class SitecoreTokenServiceLoggingTests
{
    private readonly ITestOutputHelper _output;
    private readonly IOptions<SitecoreTokenServiceOptions> _options;
    private readonly ISitecoreTokenCache _tokenCache;
    private readonly HttpClient _httpClient;

    public SitecoreTokenServiceLoggingTests(ITestOutputHelper output)
    {
        _output = output;
        _options = Options.Create(new SitecoreTokenServiceOptions());
        _tokenCache = new SitecoreTokenCache(_options);
        _httpClient = new HttpClient(new MockHttpMessageHandler(System.Net.HttpStatusCode.OK, "{}"));
    }

    [Fact]
    public async Task GetSitecoreAuthToken_ShouldLogInformation_WhenTokenIsRetrievedFromCache()
    {
        var logger = new TestLogger<SitecoreTokenService>(_output);
        var creds = new SitecoreAuthClientCredentials("test-client","secret");
        _tokenCache.SetToken(creds, new SitecoreAuthToken("cached-token", DateTimeOffset.UtcNow.AddHours(1)));
        var service = new SitecoreTokenService(_httpClient, _options, _tokenCache, logger);
        await service.GetSitecoreAuthToken(creds);
        logger.Entries.ShouldContain(e => e.Message.Contains("Token cache hit"));
    }

    [Fact]
    public async Task GetSitecoreAuthToken_ShouldLogWarning_WhenTokenIsExpired()
    {
        var logger = new TestLogger<SitecoreTokenService>(_output);
        var creds = new SitecoreAuthClientCredentials("test-client","secret");
        _tokenCache.SetToken(creds, new SitecoreAuthToken("expired-token", DateTimeOffset.UtcNow.AddSeconds(-10)));
        var httpClient = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{}"));
        var service = new SitecoreTokenService(httpClient, _options, _tokenCache, logger);
        await Assert.ThrowsAsync<SitecoreAuthResponseException>(() => service.GetSitecoreAuthToken(creds));
        logger.Entries.ShouldContain(e => e.Message.Contains("empty or missing access_token"));
    }

    [Fact]
    public async Task GetSitecoreAuthToken_ShouldLogError_WhenResponseIsUnsuccessful()
    {
        var logger = new TestLogger<SitecoreTokenService>(_output);
        var httpClient = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.BadRequest, "error"));
        var service = new SitecoreTokenService(httpClient, _options, _tokenCache, logger);
        await Assert.ThrowsAsync<SitecoreAuthHttpException>(() => service.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("test-client","secret")));
        logger.Entries.ShouldContain(e => e.Message.Contains("Authentication request failed with status"));
    }

    [Fact]
    public async Task GetSitecoreAuthToken_ShouldLogError_WhenTokenResponseIsNullOrEmpty()
    {
        var logger = new TestLogger<SitecoreTokenService>(_output);
        var httpClient = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{}"));
        var service = new SitecoreTokenService(httpClient, _options, _tokenCache, logger);
        await Assert.ThrowsAsync<SitecoreAuthResponseException>(() => service.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("test-client","secret")));
        logger.Entries.ShouldContain(e => e.Message.Contains("empty or missing access_token"));
    }

    [Fact]
    public async Task GetSitecoreAuthToken_ShouldLogError_WhenResponseIsInvalid()
    {
        var logger = new TestLogger<SitecoreTokenService>(_output);
        var httpClient = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{ invalid json }"));
        var service = new SitecoreTokenService(httpClient, _options, _tokenCache, logger);
        await Assert.ThrowsAsync<SitecoreAuthResponseException>(() => service.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("test-client","secret")));
        logger.Entries.ShouldContain(e => e.Message.Contains("Failed to parse authentication response"));
    }
}
