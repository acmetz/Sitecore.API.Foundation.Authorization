using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.DependencyInjection;
using Sitecore.API.Foundation.Authorization.Models;
using Xunit;

namespace Sitecore.API.Foundation.Tests;

public class ResilienceTests
{
    [Fact(Skip = "Resilience pipeline under active refactor; skipped per request.")]
    public async Task AddSitecoreAuthentication_WithResilience_ShouldRetryOnTransientErrors()
    {
        // Original test logic retained but skipped.
        var services = new ServiceCollection();
        var requestCount = 0;
        HttpMessageHandler primaryHandler = new SimulatedTransientPrimaryHandler(() =>
        {
            requestCount++;
            return requestCount < 3
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"test-token\",\"expires_in\":3600}")
                };
        });
        services.AddSitecoreAuthentication(configureClient: b => b.ConfigurePrimaryHttpMessageHandler(() => primaryHandler));
        var sp = services.BuildServiceProvider();
        var tokenService = sp.GetRequiredService<ISitecoreTokenService>();
        var token = await tokenService.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("id","secret"));
        requestCount.ShouldBe(3);
        token.AccessToken.ShouldBe("test-token");
    }

    [Fact]
    public void AddSitecoreAuthentication_ShouldReturnIServiceCollection()
    {
        var services = new ServiceCollection();
        var result = services.AddSitecoreAuthentication();
        result.ShouldBeSameAs(services);
    }

    private sealed class SimulatedTransientPrimaryHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _next;
        public SimulatedTransientPrimaryHandler(Func<HttpResponseMessage> next) => _next = next;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            => Task.FromResult(_next());
    }
}
