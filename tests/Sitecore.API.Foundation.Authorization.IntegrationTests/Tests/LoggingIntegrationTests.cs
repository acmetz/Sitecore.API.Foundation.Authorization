using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Shouldly;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.Exceptions;
using Sitecore.API.Foundation.Authorization.Models;
using Sitecore.API.Foundation.Authorization.Services;
using Sitecore.API.Foundation.Authorization.IntegrationTests.Mocks;
using Xunit;
using Xunit.Abstractions;

namespace Sitecore.API.Foundation.Authorization.IntegrationTests.Tests
{
    public class LoggingIntegrationTests
    {
        private readonly ITestOutputHelper _output;
        private readonly IOptions<SitecoreTokenServiceOptions> _options;
        private readonly SitecoreTokenCache _tokenCache;
        private readonly HttpClient _httpClient;
        private readonly TestLogger<SitecoreTokenService> _logger;

        public LoggingIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _options = Options.Create(new SitecoreTokenServiceOptions());
            _tokenCache = new SitecoreTokenCache(_options);
            _httpClient = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{\"access_token\":\"token\",\"expires_in\":3600}"));
            _logger = new TestLogger<SitecoreTokenService>(output);
        }

        [Fact]
        public async Task Should_log_cache_hit_and_http_flow()
        {
            var svc = new SitecoreTokenService(_httpClient, _options, _tokenCache, _logger);
            var creds = new SitecoreAuthClientCredentials("client","secret");
            // first call network
            var t1 = await svc.GetSitecoreAuthToken(creds);
            // second call cache
            var t2 = await svc.GetSitecoreAuthToken(creds);
            t1.AccessToken.ShouldNotBeEmpty();
            t2.ShouldBe(t1);
            _logger.Entries.ShouldContain(e => e.Message.Contains("Requesting new token"));
            _logger.Entries.ShouldContain(e => e.Message.Contains("Token cache hit"));
        }

        [Fact]
        public async Task Should_log_error_when_response_invalid()
        {
            var http = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{ invalid json }"));
            var svc = new SitecoreTokenService(http, _options, _tokenCache, _logger);
            var creds = new SitecoreAuthClientCredentials("client","secret");
            await Should.ThrowAsync<SitecoreAuthResponseException>(() => svc.GetSitecoreAuthToken(creds));
            _logger.Entries.ShouldContain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error && e.Message.Contains("Failed to parse authentication response"));
        }

        [Fact]
        public async Task Should_log_authentication_failure()
        {
            var http = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.BadRequest, "error"));
            var svc = new SitecoreTokenService(http, _options, _tokenCache, _logger);
            var creds = new SitecoreAuthClientCredentials("client","secret");
            await Should.ThrowAsync<SitecoreAuthHttpException>(() => svc.GetSitecoreAuthToken(creds));
            _logger.Entries.ShouldContain(e => e.Message.Contains("Authentication request failed"));
        }
    }
}
