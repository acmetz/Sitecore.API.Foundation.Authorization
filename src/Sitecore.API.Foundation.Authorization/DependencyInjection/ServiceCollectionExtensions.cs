using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.Services;

namespace Sitecore.API.Foundation.Authorization.DependencyInjection;

/// <summary>
/// Extension methods for registering Sitecore authentication services in the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Sitecore authentication services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional action to configure the token service options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSitecoreAuthentication(
        this IServiceCollection services,
        Action<SitecoreTokenServiceOptions>? configureOptions = null)
    {
        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<SitecoreTokenServiceOptions>(_ => { });
        }

        AddCoreServices(services, ServiceLifetime.Scoped);

        return services;
    }

    /// <summary>
    /// Adds Sitecore authentication services to the service collection with configuration from IConfiguration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <param name="sectionName">The configuration section name. Defaults to "SitecoreAuthentication".</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSitecoreAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "SitecoreAuthentication")
    {
        // Configure options from configuration
        services.Configure<SitecoreTokenServiceOptions>(
            configuration.GetSection(sectionName));

        AddCoreServices(services, ServiceLifetime.Scoped);

        return services;
    }

    /// <summary>
    /// Adds Sitecore authentication services to the service collection with singleton lifetime for the service.
    /// Use this when you want to share the same service instance across the entire application.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional action to configure the token service options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSitecoreAuthenticationSingleton(
        this IServiceCollection services,
        Action<SitecoreTokenServiceOptions>? configureOptions = null)
    {
        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<SitecoreTokenServiceOptions>(_ => { });
        }

        AddCoreServices(services, ServiceLifetime.Singleton);

        return services;
    }

    /// <summary>
    /// Adds Sitecore authentication services to the service collection with singleton lifetime for the service and configuration from IConfiguration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <param name="sectionName">The configuration section name. Defaults to "SitecoreAuthentication".</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSitecoreAuthenticationSingleton(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "SitecoreAuthentication")
    {
        // Configure options from configuration
        services.Configure<SitecoreTokenServiceOptions>(
            configuration.GetSection(sectionName));

        AddCoreServices(services, ServiceLifetime.Singleton);

        return services;
    }

    private static void AddCoreServices(IServiceCollection services, ServiceLifetime lifetime)
    {
        // Register HttpClient if not already registered
        services.AddHttpClient();

        // Register cache as singleton (shared across all requests)
        services.TryAddSingleton<ISitecoreTokenCache, SitecoreTokenCache>();

        // Register service with the specified lifetime
        services.TryAdd(new ServiceDescriptor(
            typeof(ISitecoreTokenService),
            typeof(SitecoreTokenService),
            lifetime));
    }
}