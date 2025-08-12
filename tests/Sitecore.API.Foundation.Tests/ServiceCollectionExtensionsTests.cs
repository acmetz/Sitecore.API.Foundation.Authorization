using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Sitecore.API.Foundation.Authorization;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.DependencyInjection;
using Sitecore.API.Foundation.Authorization.Models;
using Sitecore.API.Foundation.Authorization.Services;

namespace Sitecore.API.Foundation.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSitecoreAuthentication_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSitecoreAuthentication();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        serviceProvider.GetService<ISitecoreTokenCache>().ShouldNotBeNull();
        serviceProvider.GetService<ISitecoreTokenService>().ShouldNotBeNull();
        serviceProvider.GetService<IOptions<SitecoreTokenServiceOptions>>().ShouldNotBeNull();
    }

    [Fact]
    public void AddSitecoreAuthentication_WithOptions_ShouldConfigureOptions()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSitecoreAuthentication(options =>
        {
            options.MaxCacheSize = 20;
            options.CleanupThreshold = 25;
            options.CleanupInterval = TimeSpan.FromMinutes(10);
        });
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var options = serviceProvider.GetRequiredService<IOptions<SitecoreTokenServiceOptions>>().Value;
        options.MaxCacheSize.ShouldBe(20);
        options.CleanupThreshold.ShouldBe(25);
        options.CleanupInterval.ShouldBe(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void AddSitecoreAuthentication_WithCustomAuthUrl_ShouldConfigureAuthUrl()
    {
        // Arrange
        var services = new ServiceCollection();
        var customAuthUrl = "https://custom-auth.example.com/oauth/token";

        // Act
        services.AddSitecoreAuthentication(options =>
        {
            options.AuthTokenUrl = customAuthUrl;
        });
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var options = serviceProvider.GetRequiredService<IOptions<SitecoreTokenServiceOptions>>().Value;
        options.AuthTokenUrl.ShouldBe(customAuthUrl);
    }

    [Fact]
    public void AddSitecoreAuthentication_WithConfiguration_ShouldConfigureFromConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        var configurationData = new Dictionary<string, string>
        {
            ["SitecoreAuthentication:MaxCacheSize"] = "30",
            ["SitecoreAuthentication:CleanupThreshold"] = "35",
            ["SitecoreAuthentication:CleanupInterval"] = "00:15:00",
            ["SitecoreAuthentication:AuthTokenUrl"] = "https://config-auth.example.com/oauth/token"
        };
        
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationData!)
            .Build();

        // Act
        services.AddSitecoreAuthentication(configuration);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var options = serviceProvider.GetRequiredService<IOptions<SitecoreTokenServiceOptions>>().Value;
        options.MaxCacheSize.ShouldBe(30);
        options.CleanupThreshold.ShouldBe(35);
        options.CleanupInterval.ShouldBe(TimeSpan.FromMinutes(15));
        options.AuthTokenUrl.ShouldBe("https://config-auth.example.com/oauth/token");
    }

    [Fact]
    public void AddSitecoreAuthentication_WithConfigurationAndCustomSection_ShouldConfigureFromCustomSection()
    {
        // Arrange
        var services = new ServiceCollection();
        var configurationData = new Dictionary<string, string>
        {
            ["CustomSection:MaxCacheSize"] = "40",
            ["CustomSection:CleanupThreshold"] = "45",
            ["CustomSection:CleanupInterval"] = "00:20:00",
            ["CustomSection:AuthTokenUrl"] = "https://custom-section-auth.example.com/oauth/token"
        };
        
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationData!)
            .Build();

        // Act
        services.AddSitecoreAuthentication(configuration, "CustomSection");
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var options = serviceProvider.GetRequiredService<IOptions<SitecoreTokenServiceOptions>>().Value;
        options.MaxCacheSize.ShouldBe(40);
        options.CleanupThreshold.ShouldBe(45);
        options.CleanupInterval.ShouldBe(TimeSpan.FromMinutes(20));
        options.AuthTokenUrl.ShouldBe("https://custom-section-auth.example.com/oauth/token");
    }

    [Fact]
    public void AddSitecoreAuthentication_ShouldRegisterServiceAsScoped()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSitecoreAuthentication();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        using var scope1 = serviceProvider.CreateScope();
        using var scope2 = serviceProvider.CreateScope();

        var service1 = scope1.ServiceProvider.GetRequiredService<ISitecoreTokenService>();
        var service2 = scope2.ServiceProvider.GetRequiredService<ISitecoreTokenService>();

        service1.ShouldNotBeSameAs(service2); // Different instances for different scopes
    }

    [Fact]
    public void AddSitecoreAuthentication_ShouldRegisterCacheAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSitecoreAuthentication();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        using var scope1 = serviceProvider.CreateScope();
        using var scope2 = serviceProvider.CreateScope();

        var cache1 = scope1.ServiceProvider.GetRequiredService<ISitecoreTokenCache>();
        var cache2 = scope2.ServiceProvider.GetRequiredService<ISitecoreTokenCache>();

        cache1.ShouldBeSameAs(cache2); // Same instance across scopes
    }

    [Fact]
    public void AddSitecoreAuthenticationSingleton_ShouldRegisterServiceAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSitecoreAuthenticationSingleton();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        using var scope1 = serviceProvider.CreateScope();
        using var scope2 = serviceProvider.CreateScope();

        var service1 = scope1.ServiceProvider.GetRequiredService<ISitecoreTokenService>();
        var service2 = scope2.ServiceProvider.GetRequiredService<ISitecoreTokenService>();

        service1.ShouldBeSameAs(service2); // Same instance across scopes
    }

    [Fact]
    public void AddSitecoreAuthenticationSingleton_WithCustomAuthUrl_ShouldConfigureAuthUrl()
    {
        // Arrange
        var services = new ServiceCollection();
        var customAuthUrl = "https://singleton-auth.example.com/oauth/token";

        // Act
        services.AddSitecoreAuthenticationSingleton(options =>
        {
            options.AuthTokenUrl = customAuthUrl;
        });
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var options = serviceProvider.GetRequiredService<IOptions<SitecoreTokenServiceOptions>>().Value;
        options.AuthTokenUrl.ShouldBe(customAuthUrl);
    }
}