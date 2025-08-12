using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Sitecore.API.Foundation.Authorization;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.DependencyInjection;
using Sitecore.API.Foundation.Authorization.Models;
using Sitecore.API.Foundation.Authorization.Services;

namespace Sitecore.API.Foundation.Tests;

public class DependencyInjectionIntegrationTests
{
    [Fact]
    public void FullDIIntegration_ShouldWorkEndToEnd()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Register Sitecore authentication services
        services.AddSitecoreAuthentication(options =>
        {
            options.MaxCacheSize = 5;
            options.CleanupThreshold = 8;
            options.CleanupInterval = TimeSpan.FromMinutes(2);
        });

        var serviceProvider = services.BuildServiceProvider();

        // Act - Resolve services from DI container
        var tokenService = serviceProvider.GetRequiredService<ISitecoreTokenService>();
        var tokenCache = serviceProvider.GetRequiredService<ISitecoreTokenCache>();

        // Assert
        tokenService.ShouldNotBeNull();
        tokenService.ShouldBeOfType<SitecoreTokenService>();
        
        tokenCache.ShouldNotBeNull();
        tokenCache.ShouldBeOfType<SitecoreTokenCache>();
        tokenCache.CacheSize.ShouldBe(0);
    }

    [Fact]
    public void DISingleton_ShouldUseSameServiceInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSitecoreAuthenticationSingleton();
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var service1 = serviceProvider.GetRequiredService<ISitecoreTokenService>();
        var service2 = serviceProvider.GetRequiredService<ISitecoreTokenService>();

        // Assert
        service1.ShouldBeSameAs(service2);
    }

    [Fact]
    public void DIScoped_ShouldUseDifferentServiceInstancesAcrossScopes()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSitecoreAuthentication();
        var serviceProvider = services.BuildServiceProvider();

        // Act
        ISitecoreTokenService service1, service2;
        using (var scope1 = serviceProvider.CreateScope())
        {
            service1 = scope1.ServiceProvider.GetRequiredService<ISitecoreTokenService>();
        }
        
        using (var scope2 = serviceProvider.CreateScope())
        {
            service2 = scope2.ServiceProvider.GetRequiredService<ISitecoreTokenService>();
        }

        // Assert
        service1.ShouldNotBeSameAs(service2);
    }

    [Fact]
    public void DI_CacheShouldBeSingletonAcrossScopes()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSitecoreAuthentication();
        var serviceProvider = services.BuildServiceProvider();

        // Act
        ISitecoreTokenCache cache1, cache2;
        using (var scope1 = serviceProvider.CreateScope())
        {
            cache1 = scope1.ServiceProvider.GetRequiredService<ISitecoreTokenCache>();
        }
        
        using (var scope2 = serviceProvider.CreateScope())
        {
            cache2 = scope2.ServiceProvider.GetRequiredService<ISitecoreTokenCache>();
        }

        // Assert
        cache1.ShouldBeSameAs(cache2);
    }

    [Fact]
    public void DI_ShouldResolveAllDependencies()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSitecoreAuthentication();
        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert - Should not throw any exceptions
        var httpClientService = serviceProvider.GetService<IHttpClientFactory>();
        httpClientService.ShouldNotBeNull(); // HttpClient factory should be registered

        var tokenService = serviceProvider.GetService<ISitecoreTokenService>();
        tokenService.ShouldNotBeNull();

        var tokenCache = serviceProvider.GetService<ISitecoreTokenCache>();
        tokenCache.ShouldNotBeNull();
    }

    [Fact]
    public void DI_ConfigurationIntegration_ShouldWorkCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSitecoreAuthentication();
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var tokenService = serviceProvider.GetRequiredService<ISitecoreTokenService>();

        // Assert
        tokenService.ShouldNotBeNull();
        tokenService.ShouldBeOfType<SitecoreTokenService>();
    }
}