using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.DependencyInjection;
using Sitecore.API.Foundation.Authorization.Models;
using Sitecore.API.Foundation.Authorization.Services;

namespace Sitecore.API.Foundation.Tests;

public class DependencyInjectionIntegrationTests
{
    public sealed class TypedClientA;
    public sealed class TypedClientB;

    [Fact]
    public async Task EndToEndDI_ScopedService_ShouldAcquireShareCacheAcrossScopesAndRefresh()
    {
        // Arrange
        using var handler = new DelegateHttpMessageHandler((_, attempt) =>
            TestHttpResponseHelper.CreateTokenResponse($"di-scoped-token-{attempt}"));

        var services = new ServiceCollection();
        services.AddSitecoreAuthentication(
            configureOptions: options =>
            {
                options.MaxCacheSize = 5;
                options.CleanupThreshold = 8;
                options.CleanupInterval = TimeSpan.FromMinutes(2);
                options.AuthTokenUrl = "https://di-auth.example.com/oauth/token";
            },
            configureClient: client => client.ConfigurePrimaryHttpMessageHandler(() => handler));

        using var serviceProvider = services.BuildServiceProvider();
        serviceProvider.GetService<IHttpClientFactory>().ShouldNotBeNull();

        var credentials = new SitecoreAuthClientCredentials("di-client", "di-secret");

        // Act - Scope 1 fetches token; Scope 2 hits singleton cache; Scope 2 refreshes token
        SitecoreTokenCache concreteCache;
        SitecoreAuthToken token1;
        using (var scope1 = serviceProvider.CreateScope())
        {
            var svc1 = scope1.ServiceProvider.GetRequiredService<ISitecoreTokenService>();
            svc1.ShouldBeOfType<SitecoreTokenService>();
            concreteCache = scope1.ServiceProvider.GetRequiredService<ISitecoreTokenCache>()
                .ShouldBeOfType<SitecoreTokenCache>();
            token1 = await svc1.GetSitecoreAuthToken(credentials);
        }

        SitecoreAuthToken token2;
        SitecoreAuthToken refreshed;
        using (var scope2 = serviceProvider.CreateScope())
        {
            var svc2 = scope2.ServiceProvider.GetRequiredService<ISitecoreTokenService>();
            var cache2 = scope2.ServiceProvider.GetRequiredService<ISitecoreTokenCache>();
            cache2.ShouldBeSameAs(concreteCache);

            token2 = await svc2.GetSitecoreAuthToken(credentials);
            refreshed = await svc2.TryRefreshSitecoreAuthToken(token2);
        }

        // Assert
        token1.AccessToken.ShouldBe("di-scoped-token-1");
        token2.ShouldBe(token1);
        refreshed.AccessToken.ShouldBe("di-scoped-token-2");
        handler.CallCount.ShouldBe(2);
        concreteCache.CacheSize.ShouldBe(1);
    }

    [Fact]
    public async Task EndToEndDI_MultipleTypedClients_ShouldPartitionSharedCacheAndUseConcreteTypes()
    {
        // Arrange
        using var handlerA = new DelegateHttpMessageHandler(_ =>
            TestHttpResponseHelper.CreateTokenResponse("di-typed-a"));
        using var handlerB = new DelegateHttpMessageHandler(_ =>
            TestHttpResponseHelper.CreateTokenResponse("di-typed-b"));

        var services = new ServiceCollection();
        services.AddSitecoreAuthentication<TypedClientA>(o => o.AuthTokenUrl = "https://auth-a.example.com/oauth/token")
            .ConfigureHttpClient(b => b.ConfigurePrimaryHttpMessageHandler(() => handlerA));
        services.AddSitecoreAuthenticationSingleton<TypedClientB>(o => o.AuthTokenUrl = "https://auth-b.example.com/oauth/token")
            .ConfigureHttpClient(b => b.ConfigurePrimaryHttpMessageHandler(() => handlerB));

        using var serviceProvider = services.BuildServiceProvider();
        var svcA = serviceProvider.GetRequiredService<ISitecoreTokenService<TypedClientA>>();
        var svcB = serviceProvider.GetRequiredService<ISitecoreTokenService<TypedClientB>>();
        var sharedCache = serviceProvider.GetRequiredService<ISitecoreTokenCache>();

        svcA.ShouldBeOfType<SitecoreTokenService<TypedClientA>>();
        svcB.ShouldBeOfType<SitecoreTokenService<TypedClientB>>();

        var sharedCreds = new SitecoreAuthClientCredentials("shared-id", "shared-secret");

        // Act
        var tokenA = await svcA.GetSitecoreAuthToken(sharedCreds);
        var tokenB = await svcB.GetSitecoreAuthToken(sharedCreds);

        // Assert
        tokenA.AccessToken.ShouldBe("di-typed-a");
        tokenB.AccessToken.ShouldBe("di-typed-b");
        sharedCache.CacheSize.ShouldBe(2);
    }
}
