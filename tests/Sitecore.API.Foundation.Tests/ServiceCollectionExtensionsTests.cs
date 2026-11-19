using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.DependencyInjection;
using Sitecore.API.Foundation.Authorization.Models;

namespace Sitecore.API.Foundation.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void SitecoreTokenServiceOptions_ShouldHaveExpectedDefaultValues()
    {
        var options = new SitecoreTokenServiceOptions();

        options.MaxCacheSize.ShouldBe(10);
        options.CleanupThreshold.ShouldBe(15);
        options.CleanupInterval.ShouldBe(TimeSpan.FromMinutes(5));
        options.AuthTokenUrl.ShouldBe("https://auth.sitecorecloud.io/oauth/token");
    }

    [Fact]
    public void AddSitecoreAuthentication_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSitecoreAuthentication();
        using var serviceProvider = services.BuildServiceProvider();

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
        using var serviceProvider = services.BuildServiceProvider();

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
        using var serviceProvider = services.BuildServiceProvider();

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
        using var serviceProvider = services.BuildServiceProvider();

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
        using var serviceProvider = services.BuildServiceProvider();

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
        using var serviceProvider = services.BuildServiceProvider();

        // Assert
        using var scope1 = serviceProvider.CreateScope();
        using var scope2 = serviceProvider.CreateScope();

        var scope1Service1 = scope1.ServiceProvider.GetRequiredService<ISitecoreTokenService>();
        var scope1Service2 = scope1.ServiceProvider.GetRequiredService<ISitecoreTokenService>();
        var scope2Service = scope2.ServiceProvider.GetRequiredService<ISitecoreTokenService>();

        scope1Service1.ShouldBeSameAs(scope1Service2); // Same instance within a scope
        scope1Service1.ShouldNotBeSameAs(scope2Service); // Different instances across scopes
    }

    [Fact]
    public void AddSitecoreAuthentication_ShouldRegisterCacheAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSitecoreAuthentication();
        using var serviceProvider = services.BuildServiceProvider();

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
        using var serviceProvider = services.BuildServiceProvider();

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
        using var serviceProvider = services.BuildServiceProvider();

        // Assert
        var options = serviceProvider.GetRequiredService<IOptions<SitecoreTokenServiceOptions>>().Value;
        options.AuthTokenUrl.ShouldBe(customAuthUrl);
    }

    [Fact]
    public void AddSitecoreAuthenticationSingleton_WithConfigurationAndDefaultSection_ShouldBindOptionsAndRegisterSingleton()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SitecoreAuthentication:MaxCacheSize"] = "22",
                ["SitecoreAuthentication:CleanupThreshold"] = "33",
                ["SitecoreAuthentication:CleanupInterval"] = "00:12:00",
                ["SitecoreAuthentication:AuthTokenUrl"] = "https://singleton-cfg.example.com/oauth/token"
            })
            .Build();

        var services = new ServiceCollection();
        var returned = services.AddSitecoreAuthenticationSingleton(configuration);
        returned.ShouldBeSameAs(services);

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<SitecoreTokenServiceOptions>>().Value;
        options.MaxCacheSize.ShouldBe(22);
        options.CleanupThreshold.ShouldBe(33);
        options.CleanupInterval.ShouldBe(TimeSpan.FromMinutes(12));
        options.AuthTokenUrl.ShouldBe("https://singleton-cfg.example.com/oauth/token");

        using var scope1 = serviceProvider.CreateScope();
        using var scope2 = serviceProvider.CreateScope();
        scope1.ServiceProvider.GetRequiredService<ISitecoreTokenService>()
            .ShouldBeSameAs(scope2.ServiceProvider.GetRequiredService<ISitecoreTokenService>());
    }

    [Fact]
    public void AddSitecoreAuthenticationSingleton_WithConfigurationAndCustomSection_ShouldBindOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CustomSingleton:MaxCacheSize"] = "44",
                ["CustomSingleton:AuthTokenUrl"] = "https://singleton-custom.example.com/oauth/token"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSitecoreAuthenticationSingleton(configuration, "CustomSingleton");

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<SitecoreTokenServiceOptions>>().Value;
        options.MaxCacheSize.ShouldBe(44);
        options.AuthTokenUrl.ShouldBe("https://singleton-custom.example.com/oauth/token");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async System.Threading.Tasks.Task AddSitecoreAuthentication_WithConfigurationResilienceClientAndFactory_ShouldApplyAllCustomizations(bool asSingleton)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CustomResilient:AuthTokenUrl"] = "https://resilient-config.example.com/oauth/token"
            })
            .Build();

        var namedAttempts = 0;
        using var namedHandler = new DelegateHttpMessageHandler(_ => ++namedAttempts == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : TestHttpResponseHelper.CreateTokenResponse(asSingleton ? "singleton-resilient-tok" : "scoped-resilient-tok"));

        var resilientServices = new ServiceCollection();
        if (asSingleton)
        {
            resilientServices.AddSitecoreAuthenticationSingleton(
                configuration,
                "CustomResilient",
                resilience =>
                {
                    resilience.Retry.MaxRetryAttempts = 1;
                    resilience.Retry.Delay = TimeSpan.FromMilliseconds(1);
                },
                b => b.ConfigurePrimaryHttpMessageHandler(() => namedHandler));
        }
        else
        {
            resilientServices.AddSitecoreAuthentication(
                configuration,
                "CustomResilient",
                resilience =>
                {
                    resilience.Retry.MaxRetryAttempts = 1;
                    resilience.Retry.Delay = TimeSpan.FromMilliseconds(1);
                },
                b => b.ConfigurePrimaryHttpMessageHandler(() => namedHandler));
        }

        using (var sp = resilientServices.BuildServiceProvider())
        {
            var tokenService = sp.GetRequiredService<ISitecoreTokenService>();
            var token = await tokenService.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("cfg-id", "cfg-secret"));
            token.AccessToken.ShouldBe(asSingleton ? "singleton-resilient-tok" : "scoped-resilient-tok");
            namedAttempts.ShouldBe(2);
        }

        // Passing httpClientFactory bypasses the named HttpClient pipeline while still invoking configureClient at registration
        var factoryAttempts = 0;
        var clientCustomized = false;
        using var customHandler = new DelegateHttpMessageHandler(_ =>
        {
            factoryAttempts++;
            return TestHttpResponseHelper.CreateTokenResponse(asSingleton ? "singleton-cfg-tok" : "scoped-cfg-tok");
        });
        using var factoryHttpClient = new HttpClient(customHandler);

        var services = new ServiceCollection();
        if (asSingleton)
        {
            services.AddSitecoreAuthenticationSingleton(
                configuration,
                "CustomResilient",
                resilience =>
                {
                    resilience.Retry.MaxRetryAttempts = 1;
                    resilience.Retry.Delay = TimeSpan.FromMilliseconds(1);
                },
                _ => clientCustomized = true,
                _ => factoryHttpClient);
        }
        else
        {
            services.AddSitecoreAuthentication(
                configuration,
                "CustomResilient",
                resilience =>
                {
                    resilience.Retry.MaxRetryAttempts = 1;
                    resilience.Retry.Delay = TimeSpan.FromMilliseconds(1);
                },
                _ => clientCustomized = true,
                _ => factoryHttpClient);
        }

        using var factorySp = services.BuildServiceProvider();
        var factoryTokenService = factorySp.GetRequiredService<ISitecoreTokenService>();
        var factoryToken = await factoryTokenService.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("cfg-id", "cfg-secret"));

        clientCustomized.ShouldBeTrue();
        factoryToken.AccessToken.ShouldBe(asSingleton ? "singleton-cfg-tok" : "scoped-cfg-tok");
        factoryAttempts.ShouldBe(1);
    }

    [Fact]
    public async System.Threading.Tasks.Task AddSitecoreAuthenticationSingleton_ShouldConfigurePooledConnectionLifetime_WhileAllowingCustomPrimaryHandler()
    {
        SocketsHttpHandler? observedSocketsHandler = null;
        var services = new ServiceCollection();
        services.AddSitecoreAuthenticationSingleton(
            configureClient: b => b.ConfigurePrimaryHttpMessageHandler((handler, _) =>
            {
                observedSocketsHandler = handler as SocketsHttpHandler;
            }));

        using (var sp = services.BuildServiceProvider())
        {
            _ = sp.GetRequiredService<ISitecoreTokenService>();
        }

        observedSocketsHandler.ShouldNotBeNull();
        observedSocketsHandler.PooledConnectionLifetime.ShouldBe(TimeSpan.FromMinutes(2));

        // Custom primary handler override should replace SocketsHttpHandler cleanly without error
        using var customHandler = new DelegateHttpMessageHandler(_ => TestHttpResponseHelper.CreateTokenResponse("custom-primary-token"));
        var overrideServices = new ServiceCollection();
        overrideServices.AddSitecoreAuthenticationSingleton(
            configureClient: b => b.ConfigurePrimaryHttpMessageHandler(() => customHandler));

        using var overrideSp = overrideServices.BuildServiceProvider();
        var token = await overrideSp.GetRequiredService<ISitecoreTokenService>()
            .GetSitecoreAuthToken(new SitecoreAuthClientCredentials("id", "secret"));
        token.AccessToken.ShouldBe("custom-primary-token");
        customHandler.CallCount.ShouldBe(1);
    }

    [Fact]
    public void UntypedOverloads_WithNullOrInvalidArguments_ShouldThrowWithoutMutatingServices()
    {
        IServiceCollection? nullServices = null;
        var config = new ConfigurationBuilder().Build();

        Should.Throw<ArgumentNullException>(() => nullServices!.AddSitecoreAuthentication((Action<SitecoreTokenServiceOptions>?)null))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() => nullServices!.AddSitecoreAuthentication())
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() => nullServices!.AddSitecoreAuthentication(config, "SitecoreAuthentication"))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() => nullServices!.AddSitecoreAuthentication(config))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() => nullServices!.AddSitecoreAuthenticationSingleton((Action<SitecoreTokenServiceOptions>?)null))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() => nullServices!.AddSitecoreAuthenticationSingleton())
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() => nullServices!.AddSitecoreAuthenticationSingleton(config, "SitecoreAuthentication"))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() => nullServices!.AddSitecoreAuthenticationSingleton(config))
            .ParamName.ShouldBe("services");

        var services = new ServiceCollection();
        Should.Throw<ArgumentNullException>(() => services.AddSitecoreAuthentication((IConfiguration)null!))
            .ParamName.ShouldBe("configuration");
        Should.Throw<ArgumentNullException>(() => services.AddSitecoreAuthenticationSingleton((IConfiguration)null!))
            .ParamName.ShouldBe("configuration");
        Should.Throw<ArgumentNullException>(() => services.AddSitecoreAuthentication(config, (string)null!))
            .ParamName.ShouldBe("sectionName");
        Should.Throw<ArgumentException>(() => services.AddSitecoreAuthentication(config, ""))
            .ParamName.ShouldBe("sectionName");
        Should.Throw<ArgumentException>(() => services.AddSitecoreAuthentication(config, "   "))
            .ParamName.ShouldBe("sectionName");
        Should.Throw<ArgumentNullException>(() => services.AddSitecoreAuthenticationSingleton(config, (string)null!))
            .ParamName.ShouldBe("sectionName");
        Should.Throw<ArgumentException>(() => services.AddSitecoreAuthenticationSingleton(config, ""))
            .ParamName.ShouldBe("sectionName");
        Should.Throw<ArgumentException>(() => services.AddSitecoreAuthenticationSingleton(config, "   "))
            .ParamName.ShouldBe("sectionName");

        services.Count.ShouldBe(0);
    }
}