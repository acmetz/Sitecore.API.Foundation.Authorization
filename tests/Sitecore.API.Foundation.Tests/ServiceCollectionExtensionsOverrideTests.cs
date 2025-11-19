using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.DependencyInjection;
using Sitecore.API.Foundation.Authorization.Models;
using Sitecore.API.Foundation.Authorization.Services;
using System.Threading;
using System.Threading.Tasks;

namespace Sitecore.API.Foundation.Tests;

public class ServiceCollectionExtensionsOverrideTests
{
    private sealed class CustomTokenService : ISitecoreTokenService
    {
        public Task<SitecoreAuthToken> GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials, CancellationToken cancellationToken = default)
            => Task.FromResult(new SitecoreAuthToken("custom", DateTimeOffset.UtcNow.AddMinutes(5)));
        public Task<SitecoreAuthToken> TryRefreshSitecoreAuthToken(SitecoreAuthToken token, CancellationToken cancellationToken = default)
            => Task.FromResult(new SitecoreAuthToken("custom2", DateTimeOffset.UtcNow.AddMinutes(5)));
    }

    private sealed class CustomCache : ISitecoreTokenCache
    {
        public int CacheSize => 0;
        public void ClearCache() { }
        public void Dispose() { }
        public SitecoreAuthToken? GetToken(SitecoreAuthClientCredentials credentials) => null;
        public void PerformCleanup() { }
        public SitecoreAuthClientCredentials? RemoveToken(SitecoreAuthToken token) => null;
        public void SetToken(SitecoreAuthClientCredentials credentials, SitecoreAuthToken token) { }
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
