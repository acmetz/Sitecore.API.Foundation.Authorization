using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Sitecore.API.Foundation.Authorization.Configuration;

namespace Sitecore.API.Foundation.Authorization.DependencyInjection;

/// <summary>
/// Fluent builder for configuring a typed Sitecore authentication registration for <typeparamref name="TClient"/>.
/// </summary>
/// <typeparam name="TClient">The consumer type associated with the typed token service.</typeparam>
public interface ISitecoreAuthenticationBuilder<TClient>
    where TClient : class
{
    /// <summary>
    /// Gets the application service collection being configured.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Configures the named <see cref="SitecoreTokenServiceOptions"/> for <typeparamref name="TClient"/>.
    /// Note that global cache capacity and cleanup settings (<see cref="SitecoreTokenServiceOptions.MaxCacheSize"/>,
    /// <see cref="SitecoreTokenServiceOptions.CleanupThreshold"/>, and <see cref="SitecoreTokenServiceOptions.CleanupInterval"/>)
    /// are read by the shared singleton cache from the default unnamed options registration.
    /// </summary>
    /// <param name="configureOptions">An action to configure the options for <typeparamref name="TClient"/>.</param>
    /// <returns>The builder instance for method chaining.</returns>
    ISitecoreAuthenticationBuilder<TClient> ConfigureOptions(Action<SitecoreTokenServiceOptions> configureOptions);

    /// <summary>
    /// Configures the named <see cref="SitecoreTokenServiceOptions"/> for <typeparamref name="TClient"/> from an <see cref="IConfiguration"/> section.
    /// Note that global cache capacity and cleanup settings (<see cref="SitecoreTokenServiceOptions.MaxCacheSize"/>,
    /// <see cref="SitecoreTokenServiceOptions.CleanupThreshold"/>, and <see cref="SitecoreTokenServiceOptions.CleanupInterval"/>)
    /// are read by the shared singleton cache from the default unnamed options registration.
    /// </summary>
    /// <param name="configuration">The configuration root or section container.</param>
    /// <param name="sectionName">The configuration section name. Defaults to "SitecoreAuthentication".</param>
    /// <returns>The builder instance for method chaining.</returns>
    ISitecoreAuthenticationBuilder<TClient> ConfigureOptions(IConfiguration configuration, string sectionName = Constants.DefaultConfigurationSectionName);

    /// <summary>
    /// Adds and optionally configures standard HTTP resilience on the underlying named <see cref="HttpClient"/> for <typeparamref name="TClient"/>.
    /// Calling this method multiple times reconfigures the existing standard resilience handler rather than registering duplicate handlers.
    /// </summary>
    /// <param name="configureResilience">Optional action to configure <see cref="HttpStandardResilienceOptions"/>.</param>
    /// <returns>The builder instance for method chaining.</returns>
    ISitecoreAuthenticationBuilder<TClient> ConfigureResilience(Action<HttpStandardResilienceOptions>? configureResilience = null);

    /// <summary>
    /// Customizes the underlying <see cref="IHttpClientBuilder"/> used to create the authentication <see cref="HttpClient"/> for <typeparamref name="TClient"/>.
    /// </summary>
    /// <param name="configureClient">An action to configure the <see cref="IHttpClientBuilder"/>.</param>
    /// <returns>The builder instance for method chaining.</returns>
    ISitecoreAuthenticationBuilder<TClient> ConfigureHttpClient(Action<IHttpClientBuilder> configureClient);

    /// <summary>
    /// Supplies a custom factory delegate to create the <see cref="HttpClient"/> used by the typed token service for <typeparamref name="TClient"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Services.SitecoreTokenService{TClient}"/> does not dispose the <see cref="HttpClient"/> returned by <paramref name="httpClientFactory"/>; callers own its lifetime and should avoid allocating unpooled instances per scoped resolution.
    /// </remarks>
    /// <param name="httpClientFactory">The factory delegate that creates an <see cref="HttpClient"/> from the <see cref="IServiceProvider"/>.</param>
    /// <returns>The builder instance for method chaining.</returns>
    ISitecoreAuthenticationBuilder<TClient> UseHttpClientFactory(Func<IServiceProvider, HttpClient> httpClientFactory);
}

internal sealed class TypedHttpClientFactoryHolder
{
    internal Func<IServiceProvider, HttpClient>? Factory { get; set; }
}

internal sealed class SitecoreAuthenticationBuilder<TClient> : ISitecoreAuthenticationBuilder<TClient>
    where TClient : class
{
    private IHttpStandardResiliencePipelineBuilder? _resiliencePipelineBuilder;

    internal SitecoreAuthenticationBuilder(
        IServiceCollection services,
        IHttpClientBuilder httpClientBuilder,
        string optionsName,
        TypedHttpClientFactoryHolder clientFactoryHolder)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(httpClientBuilder);
        ArgumentNullException.ThrowIfNull(optionsName);
        ArgumentNullException.ThrowIfNull(clientFactoryHolder);

        Services = services;
        HttpClientBuilder = httpClientBuilder;
        OptionsName = optionsName;
        ClientFactoryHolder = clientFactoryHolder;
    }

    public IServiceCollection Services { get; }

    internal IHttpClientBuilder HttpClientBuilder { get; }

    internal string OptionsName { get; }

    internal TypedHttpClientFactoryHolder ClientFactoryHolder { get; }

    public ISitecoreAuthenticationBuilder<TClient> ConfigureOptions(Action<SitecoreTokenServiceOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);
        Services.Configure(OptionsName, configureOptions);
        return this;
    }

    public ISitecoreAuthenticationBuilder<TClient> ConfigureOptions(IConfiguration configuration, string sectionName = Constants.DefaultConfigurationSectionName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        Services.Configure<SitecoreTokenServiceOptions>(OptionsName, configuration.GetSection(sectionName));
        return this;
    }

    public ISitecoreAuthenticationBuilder<TClient> ConfigureResilience(Action<HttpStandardResilienceOptions>? configureResilience = null)
    {
        _resiliencePipelineBuilder ??= HttpClientBuilder.AddStandardResilienceHandler();
        if (configureResilience is not null)
        {
            _resiliencePipelineBuilder.Configure(configureResilience);
        }

        return this;
    }

    public ISitecoreAuthenticationBuilder<TClient> ConfigureHttpClient(Action<IHttpClientBuilder> configureClient)
    {
        ArgumentNullException.ThrowIfNull(configureClient);
        configureClient(HttpClientBuilder);
        return this;
    }

    public ISitecoreAuthenticationBuilder<TClient> UseHttpClientFactory(Func<IServiceProvider, HttpClient> httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ClientFactoryHolder.Factory = httpClientFactory;
        return this;
    }
}
