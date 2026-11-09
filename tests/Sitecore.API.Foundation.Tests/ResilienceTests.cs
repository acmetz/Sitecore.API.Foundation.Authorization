using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.DependencyInjection;
using Sitecore.API.Foundation.Authorization.Models;
using Xunit;

namespace Sitecore.API.Foundation.Tests;

public class ResilienceTests
{
    [Fact]
    public async Task AddSitecoreAuthentication_WithResilience_ShouldRetryOnTransientErrors()
    {
        // Arrange
        var services = new ServiceCollection();
        var requestCount = 0;

        services.AddSitecoreAuthentication(
            configureResilience: resilience =>
            {
                resilience.Retry.MaxRetryAttempts = 3;
                resilience.Retry.Delay = TimeSpan.FromMilliseconds(1);
                resilience.Retry.UseJitter = false;
            },
            configureClient: builder => builder.ConfigurePrimaryHttpMessageHandler(() => new SimulatedTransientPrimaryHandler(_ =>
            {
                var current = Interlocked.Increment(ref requestCount);
                return current < 3
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"access_token\":\"test-token\",\"expires_in\":3600}", Encoding.UTF8, "application/json")
                    };
            })));

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ISitecoreTokenService>();

        // Act
        var token = await tokenService.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("id", "secret"));

        // Assert
        requestCount.ShouldBe(3);
        token.AccessToken.ShouldBe("test-token");
    }

    [Fact]
    public async Task AddSitecoreAuthenticationSingleton_WithResilience_ShouldPreserveSingletonLifetimeAndRetry()
    {
        // Arrange
        var services = new ServiceCollection();
        var requestCount = 0;

        services.AddSitecoreAuthenticationSingleton(
            configureResilience: resilience =>
            {
                resilience.Retry.MaxRetryAttempts = 3;
                resilience.Retry.Delay = TimeSpan.FromMilliseconds(1);
                resilience.Retry.UseJitter = false;
            },
            configureClient: builder => builder.ConfigurePrimaryHttpMessageHandler(() => new SimulatedTransientPrimaryHandler(_ =>
            {
                var current = Interlocked.Increment(ref requestCount);
                return current < 3
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"access_token\":\"singleton-token\",\"expires_in\":3600}", Encoding.UTF8, "application/json")
                    };
            })));

        var descriptor = services.Single(d => d.ServiceType == typeof(ISitecoreTokenService));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);

        using var sp = services.BuildServiceProvider();
        using var scope1 = sp.CreateScope();
        using var scope2 = sp.CreateScope();
        var service1 = scope1.ServiceProvider.GetRequiredService<ISitecoreTokenService>();
        var service2 = scope2.ServiceProvider.GetRequiredService<ISitecoreTokenService>();

        // Act
        var token = await service1.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("singleton-id", "secret"));

        // Assert
        service1.ShouldBeSameAs(service2);
        requestCount.ShouldBe(3);
        token.AccessToken.ShouldBe("singleton-token");
    }

    [Fact]
    public async Task AddSitecoreAuthentication_WithConfigureClient_ShouldCustomizeNamedHttpClient()
    {
        // Arrange
        var services = new ServiceCollection();
        string? observedHeader = null;

        services.AddSitecoreAuthentication(
            configureClient: builder =>
            {
                builder.ConfigureHttpClient(client => client.DefaultRequestHeaders.Add("X-Sitecore-Test", "configured-client"));
                builder.ConfigurePrimaryHttpMessageHandler(() => new SimulatedTransientPrimaryHandler(request =>
                {
                    if (request.Headers.TryGetValues("X-Sitecore-Test", out var values))
                    {
                        observedHeader = values.FirstOrDefault();
                    }

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"access_token\":\"client-token\",\"expires_in\":3600}", Encoding.UTF8, "application/json")
                    };
                }));
            });

        using var sp = services.BuildServiceProvider();
        var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var namedClient = clientFactory.CreateClient(Constants.SitecoreHttpClientName);
        namedClient.DefaultRequestHeaders.Contains("X-Sitecore-Test").ShouldBeTrue();

        var tokenService = sp.GetRequiredService<ISitecoreTokenService>();

        // Act
        var token = await tokenService.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("client-id", "secret"));

        // Assert
        observedHeader.ShouldBe("configured-client");
        token.AccessToken.ShouldBe("client-token");
    }

    [Fact]
    public async Task AddSitecoreAuthentication_WithHttpClientFactory_ShouldUseCustomHttpClient()
    {
        // Arrange
        var services = new ServiceCollection();
        var customHandlerInvoked = false;
        using var customHttpClient = new HttpClient(new SimulatedTransientPrimaryHandler(_ =>
        {
            customHandlerInvoked = true;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"factory-token\",\"expires_in\":3600}", Encoding.UTF8, "application/json")
            };
        }));

        services.AddSitecoreAuthentication(httpClientFactory: _ => customHttpClient);

        using var sp = services.BuildServiceProvider();
        var tokenService = sp.GetRequiredService<ISitecoreTokenService>();

        // Act
        var token = await tokenService.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("factory-id", "secret"));

        // Assert
        customHandlerInvoked.ShouldBeTrue();
        token.AccessToken.ShouldBe("factory-token");
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
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _next;

        public SimulatedTransientPrimaryHandler(Func<HttpRequestMessage, HttpResponseMessage> next)
        {
            _next = next;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_next(request));
    }
}
