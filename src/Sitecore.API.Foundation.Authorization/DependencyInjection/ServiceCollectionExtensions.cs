using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
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
    private static readonly ConditionalWeakTable<IServiceCollection, ConcurrentDictionary<Type, object>> TypedBuilders = new();

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
    /// <param name="httpClientFactory">Optional factory delegate to supply a custom <see cref="HttpClient"/> instance. Callers own the returned instance's disposal.</param>
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
    /// <param name="httpClientFactory">Optional factory delegate to supply a custom <see cref="HttpClient"/> instance. Callers own the returned instance's disposal.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSitecoreAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = Constants.DefaultConfigurationSectionName,
        Action<HttpStandardResilienceOptions>? configureResilience = null,
        Action<IHttpClientBuilder>? configureClient = null,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

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
    /// <param name="httpClientFactory">Optional factory delegate to supply a custom <see cref="HttpClient"/> instance. Callers own the returned instance's disposal.</param>
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
    /// <param name="httpClientFactory">Optional factory delegate to supply a custom <see cref="HttpClient"/> instance. Callers own the returned instance's disposal.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSitecoreAuthenticationSingleton(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = Constants.DefaultConfigurationSectionName,
        Action<HttpStandardResilienceOptions>? configureResilience = null,
        Action<IHttpClientBuilder>? configureClient = null,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        services.Configure<SitecoreTokenServiceOptions>(configuration.GetSection(sectionName));
        return AddCoreServices(services, ServiceLifetime.Singleton, configureResilience, configureClient, httpClientFactory);
    }

    /// <summary>
    /// Adds a typed Sitecore authentication service (<see cref="ISitecoreTokenService{TClient}"/>) with scoped lifetime to the service collection.
    /// </summary>
    /// <typeparam name="TClient">The consumer type associated with the typed token service.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional action to configure the named token service options for <typeparamref name="TClient"/>.</param>
    /// <returns>An <see cref="ISitecoreAuthenticationBuilder{TClient}"/> for further configuring options, resilience, and <see cref="HttpClient"/> behavior.</returns>
    public static ISitecoreAuthenticationBuilder<TClient> AddSitecoreAuthentication<TClient>(
        this IServiceCollection services,
        Action<SitecoreTokenServiceOptions>? configureOptions = null)
        where TClient : class
        => AddTypedAuthentication<TClient>(services, configureOptions, ServiceLifetime.Scoped);

    /// <summary>
    /// Adds a typed Sitecore authentication service (<see cref="ISitecoreTokenService{TClient}"/>) with scoped lifetime and configuration from <see cref="IConfiguration"/>.
    /// </summary>
    /// <typeparam name="TClient">The consumer type associated with the typed token service.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <param name="sectionName">The configuration section name. Defaults to "SitecoreAuthentication".</param>
    /// <returns>An <see cref="ISitecoreAuthenticationBuilder{TClient}"/> for further configuring options, resilience, and <see cref="HttpClient"/> behavior.</returns>
    public static ISitecoreAuthenticationBuilder<TClient> AddSitecoreAuthentication<TClient>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = Constants.DefaultConfigurationSectionName)
        where TClient : class
        => AddTypedAuthentication<TClient>(services, configuration, sectionName, ServiceLifetime.Scoped);

    /// <summary>
    /// Adds a typed Sitecore authentication service (<see cref="ISitecoreTokenService{TClient}"/>) with singleton lifetime to the service collection.
    /// </summary>
    /// <typeparam name="TClient">The consumer type associated with the typed token service.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional action to configure the named token service options for <typeparamref name="TClient"/>.</param>
    /// <returns>An <see cref="ISitecoreAuthenticationBuilder{TClient}"/> for further configuring options, resilience, and <see cref="HttpClient"/> behavior.</returns>
    public static ISitecoreAuthenticationBuilder<TClient> AddSitecoreAuthenticationSingleton<TClient>(
        this IServiceCollection services,
        Action<SitecoreTokenServiceOptions>? configureOptions = null)
        where TClient : class
        => AddTypedAuthentication<TClient>(services, configureOptions, ServiceLifetime.Singleton);

    /// <summary>
    /// Adds a typed Sitecore authentication service (<see cref="ISitecoreTokenService{TClient}"/>) with singleton lifetime and configuration from <see cref="IConfiguration"/>.
    /// </summary>
    /// <typeparam name="TClient">The consumer type associated with the typed token service.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <param name="sectionName">The configuration section name. Defaults to "SitecoreAuthentication".</param>
    /// <returns>An <see cref="ISitecoreAuthenticationBuilder{TClient}"/> for further configuring options, resilience, and <see cref="HttpClient"/> behavior.</returns>
    public static ISitecoreAuthenticationBuilder<TClient> AddSitecoreAuthenticationSingleton<TClient>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = Constants.DefaultConfigurationSectionName)
        where TClient : class
        => AddTypedAuthentication<TClient>(services, configuration, sectionName, ServiceLifetime.Singleton);

    private static ISitecoreAuthenticationBuilder<TClient> AddTypedAuthentication<TClient>(
        IServiceCollection services,
        Action<SitecoreTokenServiceOptions>? configureOptions,
        ServiceLifetime lifetime)
        where TClient : class
    {
        var builder = AddTypedCoreServices<TClient>(services, lifetime);
        if (configureOptions is not null)
        {
            builder.ConfigureOptions(configureOptions);
        }

        return builder;
    }

    private static ISitecoreAuthenticationBuilder<TClient> AddTypedAuthentication<TClient>(
        IServiceCollection services,
        IConfiguration configuration,
        string sectionName,
        ServiceLifetime lifetime)
        where TClient : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        var builder = AddTypedCoreServices<TClient>(services, lifetime);
        return builder.ConfigureOptions(configuration, sectionName);
    }

    private static void ConfigureOptionsDelegate(
        IServiceCollection services,
        Action<SitecoreTokenServiceOptions>? configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.AddOptions<SitecoreTokenServiceOptions>();
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
        if (!IsServiceRegistered<ISitecoreTokenService>(services))
        {
            ConfigureSingletonConnectionLifetime(httpClientBuilder, lifetime);
        }

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

    private static SitecoreAuthenticationBuilder<TClient> AddTypedCoreServices<TClient>(
        IServiceCollection services,
        ServiceLifetime lifetime)
        where TClient : class
    {
        ArgumentNullException.ThrowIfNull(services);

        var typeName = typeof(TClient).AssemblyQualifiedName ?? typeof(TClient).FullName ?? typeof(TClient).Name;
        var clientName = $"{Constants.SitecoreHttpClientName}.{typeName}";
        var optionsName = $"{Constants.SitecoreOptionsNamePrefix}.{typeName}";

        services.AddOptions<SitecoreTokenServiceOptions>();
        var httpClientBuilder = services.AddHttpClient(clientName);

        services.TryAddSingleton<ISitecoreTokenCache, SitecoreTokenCache>();

        var (builder, isNew) = GetOrAddTypedBuilder<TClient>(services, httpClientBuilder, optionsName);
        if (isNew)
        {
            ConfigureSingletonConnectionLifetime(httpClientBuilder, lifetime);
        }

        var clientFactoryHolder = builder.ClientFactoryHolder;

        services.TryAdd(new ServiceDescriptor(
            typeof(ISitecoreTokenService<TClient>),
            sp => new SitecoreTokenService<TClient>(
                clientFactoryHolder.Factory is not null
                    ? clientFactoryHolder.Factory(sp)
                    : sp.GetRequiredService<IHttpClientFactory>().CreateClient(clientName),
                sp.GetRequiredService<ISitecoreTokenCache>(),
                Options.Create(sp.GetRequiredService<IOptionsMonitor<SitecoreTokenServiceOptions>>().Get(optionsName)),
                sp.GetService<ILogger<SitecoreTokenService<TClient>>>()),
            lifetime));

        return builder;
    }

    private static (SitecoreAuthenticationBuilder<TClient> Builder, bool IsNew) GetOrAddTypedBuilder<TClient>(
        IServiceCollection services,
        IHttpClientBuilder httpClientBuilder,
        string optionsName)
        where TClient : class
    {
        var builders = TypedBuilders.GetOrCreateValue(services);
        var created = false;
        var builder = (SitecoreAuthenticationBuilder<TClient>)builders.GetOrAdd(
            typeof(TClient),
            _ =>
            {
                created = true;
                return new SitecoreAuthenticationBuilder<TClient>(
                    services,
                    httpClientBuilder,
                    optionsName,
                    new TypedHttpClientFactoryHolder());
            });

        return (builder, created);
    }

    private static bool IsServiceRegistered<TService>(IServiceCollection services)
    {
        for (var i = 0; i < services.Count; i++)
        {
            if (services[i].ServiceType == typeof(TService))
            {
                return true;
            }
        }

        return false;
    }

    private static void ConfigureSingletonConnectionLifetime(IHttpClientBuilder httpClientBuilder, ServiceLifetime lifetime)
    {
        if (lifetime != ServiceLifetime.Singleton)
        {
            return;
        }

        httpClientBuilder.ConfigurePrimaryHttpMessageHandler(
            static () => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
    }
}