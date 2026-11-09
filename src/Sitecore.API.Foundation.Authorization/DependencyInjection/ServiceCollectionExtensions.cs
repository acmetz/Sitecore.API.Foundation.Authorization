using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.Services;

namespace Sitecore.API.Foundation.Authorization.DependencyInjection;

/// <summary>
/// Extension methods for registering Sitecore authentication services in the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    private const string DefaultConfigurationSectionName = "SitecoreAuthentication";

    /// <summary>
    /// Adds Sitecore authentication services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional action to configure the token service options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSitecoreAuthentication(
        this IServiceCollection services,
        Action<SitecoreTokenServiceOptions>? configureOptions)
        => services.AddSitecoreAuthentication(configureOptions, null, null, null);

    /// <summary>
    /// Adds Sitecore authentication services to the service collection with optional HTTP resilience and client customization.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional action to configure the token service options.</param>
    /// <param name="configureResilience">Optional action to configure standard HTTP resilience options.</param>
    /// <param name="configureClient">Optional action to customize the underlying <see cref="IHttpClientBuilder"/>.</param>
    /// <param name="httpClientFactory">Optional factory delegate to supply a custom <see cref="HttpClient"/> instance.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSitecoreAuthentication(
        this IServiceCollection services,
        Action<SitecoreTokenServiceOptions>? configureOptions = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null,
        Action<IHttpClientBuilder>? configureClient = null,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null)
    {
        ConfigureOptionsDelegate(services, configureOptions);
        return AddCoreServices(services, ServiceLifetime.Scoped, configureResilience, configureClient, httpClientFactory);
    }

    /// <summary>
    /// Adds Sitecore authentication services to the service collection with configuration from <see cref="IConfiguration"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <param name="sectionName">The configuration section name.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSitecoreAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        => services.AddSitecoreAuthentication(configuration, sectionName, null, null, null);

    /// <summary>
    /// Adds Sitecore authentication services to the service collection with configuration from <see cref="IConfiguration"/> and optional HTTP resilience.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <param name="sectionName">The configuration section name. Defaults to "SitecoreAuthentication".</param>
    /// <param name="configureResilience">Optional action to configure standard HTTP resilience options.</param>
    /// <param name="configureClient">Optional action to customize the underlying <see cref="IHttpClientBuilder"/>.</param>
    /// <param name="httpClientFactory">Optional factory delegate to supply a custom <see cref="HttpClient"/> instance.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSitecoreAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = DefaultConfigurationSectionName,
        Action<HttpStandardResilienceOptions>? configureResilience = null,
        Action<IHttpClientBuilder>? configureClient = null,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null)
    {
        services.Configure<SitecoreTokenServiceOptions>(configuration.GetSection(sectionName));
        return AddCoreServices(services, ServiceLifetime.Scoped, configureResilience, configureClient, httpClientFactory);
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
        Action<SitecoreTokenServiceOptions>? configureOptions)
        => services.AddSitecoreAuthenticationSingleton(configureOptions, null, null, null);

    /// <summary>
    /// Adds Sitecore authentication services to the service collection with singleton lifetime for the service and optional HTTP resilience.
    /// Use this when you want to share the same service instance across the entire application.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional action to configure the token service options.</param>
    /// <param name="configureResilience">Optional action to configure standard HTTP resilience options.</param>
    /// <param name="configureClient">Optional action to customize the underlying <see cref="IHttpClientBuilder"/>.</param>
    /// <param name="httpClientFactory">Optional factory delegate to supply a custom <see cref="HttpClient"/> instance.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSitecoreAuthenticationSingleton(
        this IServiceCollection services,
        Action<SitecoreTokenServiceOptions>? configureOptions = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null,
        Action<IHttpClientBuilder>? configureClient = null,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null)
    {
        ConfigureOptionsDelegate(services, configureOptions);
        return AddCoreServices(services, ServiceLifetime.Singleton, configureResilience, configureClient, httpClientFactory);
    }

    /// <summary>
    /// Adds Sitecore authentication services to the service collection with singleton lifetime for the service and configuration from <see cref="IConfiguration"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <param name="sectionName">The configuration section name.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSitecoreAuthenticationSingleton(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        => services.AddSitecoreAuthenticationSingleton(configuration, sectionName, null, null, null);

    /// <summary>
    /// Adds Sitecore authentication services to the service collection with singleton lifetime for the service, configuration from <see cref="IConfiguration"/>, and optional HTTP resilience.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <param name="sectionName">The configuration section name. Defaults to "SitecoreAuthentication".</param>
    /// <param name="configureResilience">Optional action to configure standard HTTP resilience options.</param>
    /// <param name="configureClient">Optional action to customize the underlying <see cref="IHttpClientBuilder"/>.</param>
    /// <param name="httpClientFactory">Optional factory delegate to supply a custom <see cref="HttpClient"/> instance.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSitecoreAuthenticationSingleton(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = DefaultConfigurationSectionName,
        Action<HttpStandardResilienceOptions>? configureResilience = null,
        Action<IHttpClientBuilder>? configureClient = null,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null)
    {
        services.Configure<SitecoreTokenServiceOptions>(configuration.GetSection(sectionName));
        return AddCoreServices(services, ServiceLifetime.Singleton, configureResilience, configureClient, httpClientFactory);
    }

    private static void ConfigureOptionsDelegate(
        IServiceCollection services,
        Action<SitecoreTokenServiceOptions>? configureOptions)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<SitecoreTokenServiceOptions>(_ => { });
        }
    }

    private static IServiceCollection AddCoreServices(
        IServiceCollection services,
        ServiceLifetime lifetime,
        Action<HttpStandardResilienceOptions>? configureResilience,
        Action<IHttpClientBuilder>? configureClient,
        Func<IServiceProvider, HttpClient>? httpClientFactory)
    {
        // Register named HttpClient with optional standard resilience and custom client configuration
        var httpClientBuilder = services.AddHttpClient(Constants.SitecoreHttpClientName);
        if (configureResilience is not null)
        {
            httpClientBuilder.AddStandardResilienceHandler().Configure(configureResilience);
        }

        configureClient?.Invoke(httpClientBuilder);

        // Register cache as singleton (shared across all requests)
        services.TryAddSingleton<ISitecoreTokenCache, SitecoreTokenCache>();

        // Register service with the specified lifetime while respecting pre-registered user implementations
        services.TryAdd(new ServiceDescriptor(
            typeof(ISitecoreTokenService),
            sp => new SitecoreTokenService(
                httpClientFactory != null
                    ? httpClientFactory(sp)
                    : sp.GetRequiredService<IHttpClientFactory>().CreateClient(Constants.SitecoreHttpClientName),
                sp.GetRequiredService<ISitecoreTokenCache>(),
                sp.GetRequiredService<IOptions<SitecoreTokenServiceOptions>>(),
                sp.GetService<ILogger<SitecoreTokenService>>()),
            lifetime));

        return services;
    }
}