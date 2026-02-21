using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.Services;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Sitecore.API.Foundation.Authorization.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSitecoreAuthentication(
        this IServiceCollection services,
        Action<SitecoreTokenServiceOptions>? configureOptions = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null,
        Action<IHttpClientBuilder>? configureClient = null,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null)
    {
        return services.AddSitecoreAuthenticationInternal(
            configureOptions,
            configureResilience,
            configureClient,
            httpClientFactory,
            ServiceLifetime.Scoped);
    }

    public static IServiceCollection AddSitecoreAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "SitecoreAuthentication",
        Action<HttpStandardResilienceOptions>? configureResilience = null,
        Action<IHttpClientBuilder>? configureClient = null,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null)
    {
        return services.AddSitecoreAuthenticationInternal(
            options => configuration.GetSection(sectionName).Bind(options),
            configureResilience,
            configureClient,
            httpClientFactory,
            ServiceLifetime.Scoped);
    }

    public static IServiceCollection AddSitecoreAuthenticationSingleton(
        this IServiceCollection services,
        Action<SitecoreTokenServiceOptions>? configureOptions = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null,
        Action<IHttpClientBuilder>? configureClient = null,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null)
    {
        return services.AddSitecoreAuthenticationInternal(
            configureOptions,
            configureResilience,
            configureClient,
            httpClientFactory,
            ServiceLifetime.Singleton);
    }

    public static IServiceCollection AddSitecoreAuthenticationSingleton(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "SitecoreAuthentication",
        Action<HttpStandardResilienceOptions>? configureResilience = null,
        Action<IHttpClientBuilder>? configureClient = null,
        Func<IServiceProvider, HttpClient>? httpClientFactory = null)
    {
        return services.AddSitecoreAuthenticationInternal(
            options => configuration.GetSection(sectionName).Bind(options),
            configureResilience,
            configureClient,
            httpClientFactory,
            ServiceLifetime.Singleton);
    }

    private static IServiceCollection AddSitecoreAuthenticationInternal(
        this IServiceCollection services,
        Action<SitecoreTokenServiceOptions>? configureOptions,
        Action<HttpStandardResilienceOptions>? configureResilience,
        Action<IHttpClientBuilder>? configureClient,
        Func<IServiceProvider, HttpClient>? httpClientFactory,
        ServiceLifetime lifetime)
    {
        if (configureOptions != null)
            services.Configure(configureOptions);
        else
            services.Configure<SitecoreTokenServiceOptions>(_ => { });

        services.TryAddSingleton<ISitecoreTokenCache, SitecoreTokenCache>();

        var userHasService = services.Any(d => d.ServiceType == typeof(ISitecoreTokenService));
        if (userHasService)
        {
            // Respect user-registered service; do not register our own typed client.
            return services;
        }

        if (httpClientFactory is not null)
        {
            services.TryAdd(new ServiceDescriptor(typeof(ISitecoreTokenService), sp =>
            {
                var client = httpClientFactory(sp);
                var opts = sp.GetRequiredService<IOptions<SitecoreTokenServiceOptions>>();
                var cache = sp.GetRequiredService<ISitecoreTokenCache>();
                var logger = sp.GetRequiredService<ILogger<SitecoreTokenService>>();
                return new SitecoreTokenService(client, opts, cache, logger);
            }, lifetime));
            return services;
        }

        var httpClientBuilder = services.AddHttpClient<ISitecoreTokenService, SitecoreTokenService>();
        services.TryAddEnumerable(ServiceDescriptor.Describe(typeof(ISitecoreTokenService), typeof(SitecoreTokenService), lifetime));

        if (lifetime == ServiceLifetime.Singleton)
        {
            var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(ISitecoreTokenService));
            if (descriptor?.ImplementationFactory != null)
            {
                services.Remove(descriptor);
                services.Add(new ServiceDescriptor(descriptor.ServiceType, descriptor.ImplementationFactory, ServiceLifetime.Singleton));
            }
        }

        if (configureResilience != null)
            httpClientBuilder.AddStandardResilienceHandler().Configure(configureResilience);

        configureClient?.Invoke(httpClientBuilder);

        return services;
    }
}