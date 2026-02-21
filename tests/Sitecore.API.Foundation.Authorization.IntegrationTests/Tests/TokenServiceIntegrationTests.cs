using Microsoft.Extensions.Options;
using Shouldly;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.Exceptions;
using Sitecore.API.Foundation.Authorization.Models;
using Sitecore.API.Foundation.Authorization.Services;
using Sitecore.API.Foundation.Authorization.IntegrationTests.Mocks;
using Xunit;
using Xunit.Abstractions;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System;

namespace Sitecore.API.Foundation.Authorization.IntegrationTests.Tests
{
    // Simplified integration tests using mock handlers only (real Keycloak fixture removed).
    public class TokenServiceIntegrationTests : IAsyncDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly IOptions<SitecoreTokenServiceOptions> _options;
        private readonly SitecoreTokenCache _cache;
        private readonly TestLogger<SitecoreTokenService> _logger;

        public TokenServiceIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _options = Options.Create(new SitecoreTokenServiceOptions());
            _cache = new SitecoreTokenCache(_options);
            _logger = new TestLogger<SitecoreTokenService>(output);
        }

        private SitecoreTokenService Create(HttpMessageHandler handler) => new(new HttpClient(handler), _options, _cache, _logger);

        [Fact]
        public async Task Should_get_token_with_mock_handler()
        {
            var handler = new Mocks.MockHttpMessageHandler(HttpStatusCode.OK, "{\"access_token\":\"abc123\",\"expires_in\":3600}");
            var svc = Create(handler);
            var token = await svc.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("client","secret"));
            token.AccessToken.ShouldBe("abc123");
            token.IsExpired.ShouldBeFalse();
        }

        [Fact]
        public async Task Should_error_on_http_failure()
        {
            var handler = new Mocks.MockHttpMessageHandler(HttpStatusCode.BadRequest, "bad");
            var svc = Create(handler);
            await Should.ThrowAsync<SitecoreAuthHttpException>(() => svc.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("client","secret")));
        }

        [Fact]
        public async Task Should_error_on_invalid_json()
        {
            var handler = new Mocks.MockHttpMessageHandler(HttpStatusCode.OK, "{ invalid ");
            var svc = Create(handler);
            await Should.ThrowAsync<SitecoreAuthResponseException>(() => svc.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("client","secret")));
        }

        public ValueTask DisposeAsync()
        {
            _cache.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}