using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.DependencyInjection;
using Sitecore.API.Foundation.Authorization.Exceptions;
using Sitecore.API.Foundation.Authorization.Models;

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
            configureClient: builder => builder.ConfigurePrimaryHttpMessageHandler(() => new DelegateHttpMessageHandler(_ =>
            {
                var current = Interlocked.Increment(ref requestCount);
                return current < 3
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : TestHttpResponseHelper.CreateTokenResponse("test-token");
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
            configureClient: builder => builder.ConfigurePrimaryHttpMessageHandler(() => new DelegateHttpMessageHandler(_ =>
            {
                var current = Interlocked.Increment(ref requestCount);
                return current < 3
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : TestHttpResponseHelper.CreateTokenResponse("singleton-token");
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
                builder.ConfigurePrimaryHttpMessageHandler(() => new DelegateHttpMessageHandler(request =>
                {
                    if (request.Headers.TryGetValues("X-Sitecore-Test", out var values))
                    {
                        observedHeader = values.FirstOrDefault();
                    }

                    return TestHttpResponseHelper.CreateTokenResponse("client-token");
                }));
            });

        using var sp = services.BuildServiceProvider();
        var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
        using var namedClient = clientFactory.CreateClient(Constants.SitecoreHttpClientName);
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
        using var customHandler = new DelegateHttpMessageHandler(_ =>
        {
            customHandlerInvoked = true;
            return TestHttpResponseHelper.CreateTokenResponse("factory-token");
        });
        using var customHttpClient = new HttpClient(customHandler);

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

    [Fact]
    public async Task AddSitecoreAuthentication_WhenRetriesExhausted_ShouldThrowSitecoreAuthHttpException()
    {
        var services = new ServiceCollection();
        var requestCount = 0;

        services.AddSitecoreAuthentication(
            configureResilience: resilience =>
            {
                resilience.Retry.MaxRetryAttempts = 2;
                resilience.Retry.Delay = TimeSpan.FromMilliseconds(1);
                resilience.Retry.UseJitter = false;
            },
            configureClient: builder => builder.ConfigurePrimaryHttpMessageHandler(() => new DelegateHttpMessageHandler(_ =>
            {
                Interlocked.Increment(ref requestCount);
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            })));

        using var sp = services.BuildServiceProvider();
        var tokenService = sp.GetRequiredService<ISitecoreTokenService>();

        var ex = await Should.ThrowAsync<SitecoreAuthHttpException>(
            () => tokenService.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("id", "secret")));

        ex.StatusCode.ShouldBe((int)HttpStatusCode.ServiceUnavailable);
        requestCount.ShouldBe(3); // 1 initial attempt + 2 retries
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task AddSitecoreAuthentication_WithNonTransientError_ShouldNotRetry(HttpStatusCode nonTransientStatus)
    {
        var services = new ServiceCollection();
        var requestCount = 0;

        services.AddSitecoreAuthentication(
            configureResilience: resilience =>
            {
                resilience.Retry.MaxRetryAttempts = 3;
                resilience.Retry.Delay = TimeSpan.FromMilliseconds(1);
            },
            configureClient: builder => builder.ConfigurePrimaryHttpMessageHandler(() => new DelegateHttpMessageHandler(_ =>
            {
                Interlocked.Increment(ref requestCount);
                return new HttpResponseMessage(nonTransientStatus);
            })));

        using var sp = services.BuildServiceProvider();
        var tokenService = sp.GetRequiredService<ISitecoreTokenService>();

        var ex = await Should.ThrowAsync<SitecoreAuthHttpException>(
            () => tokenService.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("id", "secret")));

        ex.StatusCode.ShouldBe((int)nonTransientStatus);
        requestCount.ShouldBe(1);
    }
}
