using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.DependencyInjection;
using Sitecore.API.Foundation.Authorization.Models;
using Sitecore.API.Foundation.Authorization.Services;
using Sitecore.API.Foundation.Tests.Mocks;
using Xunit;

namespace Sitecore.API.Foundation.Tests;

public class ServiceCollectionExtensionsOverrideTests
{
    private class CustomTokenService : ISitecoreTokenService
    {
        public Task<SitecoreAuthToken> GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials, CancellationToken cancellationToken = default)
        {
            var token = new SitecoreAuthToken("custom", DateTimeOffset.UtcNow.AddMinutes(30));
            return Task.FromResult(token);
        }

        public Task<SitecoreAuthToken> TryRefreshSitecoreAuthToken(SitecoreAuthToken token, CancellationToken cancellationToken = default)
        {
            var refreshed = new SitecoreAuthToken("custom_refreshed", DateTimeOffset.UtcNow.AddHours(1));
            return Task.FromResult(refreshed);
        }
    }

    private class CustomCache : ISitecoreTokenCache
    {
        public int CacheSize => 0;
        public SitecoreAuthToken? GetToken(SitecoreAuthClientCredentials credentials)
        {
            return new SitecoreAuthToken("custom_cache", DateTimeOffset.UtcNow.AddHours(1));
        }
        public void SetToken(SitecoreAuthClientCredentials credentials, SitecoreAuthToken token) { }
        public SitecoreAuthClientCredentials? RemoveToken(SitecoreAuthToken token) => null;
        public void ClearCache() { }
        public void PerformCleanup() { }
        public void Dispose() { }
    }

    [Fact]
    public void AddSitecoreAuthentication_ShouldNotOverride_UserRegistered_Service()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISitecoreTokenService, CustomTokenService>();
        services.AddSitecoreAuthentication();
        var sp = services.BuildServiceProvider();

        var resolved = sp.GetRequiredService<ISitecoreTokenService>();
        resolved.ShouldBeOfType<CustomTokenService>();
    }

    [Fact]
    public void AddSitecoreAuthentication_ShouldNotOverride_UserRegistered_Cache()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISitecoreTokenCache, CustomCache>();

        services.AddSitecoreAuthentication();
        var sp = services.BuildServiceProvider();

        var resolved = sp.GetRequiredService<ISitecoreTokenCache>();
        resolved.ShouldBeOfType<CustomCache>();
    }

    [Fact]
    public void AddSitecoreAuthenticationSingleton_ShouldNotOverride_UserRegistered_Service()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISitecoreTokenService, CustomTokenService>();
        services.AddSitecoreAuthenticationSingleton();
        var sp = services.BuildServiceProvider();

        var resolved = sp.GetRequiredService<ISitecoreTokenService>();
        resolved.ShouldBeOfType<CustomTokenService>();
    }

    [Fact]
    public void AddSitecoreAuthenticationSingleton_ShouldNotOverride_UserRegistered_Cache()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISitecoreTokenCache, CustomCache>();

        services.AddSitecoreAuthenticationSingleton();
        var sp = services.BuildServiceProvider();

        var resolved = sp.GetRequiredService<ISitecoreTokenCache>();
        resolved.ShouldBeOfType<CustomCache>();
    }

    [Fact]
    public void AddSitecoreAuthentication_ShouldConfigureOptions_EvenWhenServicesPreRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISitecoreTokenService, CustomTokenService>();
        services.AddSingleton<ISitecoreTokenCache, CustomCache>();

        services.AddSitecoreAuthentication(o =>
        {
            o.MaxCacheSize = 42;
        });

        var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<SitecoreTokenServiceOptions>>().Value;
        opts.MaxCacheSize.ShouldBe(42);
    }
}
