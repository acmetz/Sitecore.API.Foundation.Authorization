using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.DependencyInjection;
using Sitecore.API.Foundation.Authorization.Exceptions;
using Sitecore.API.Foundation.Authorization.Models;
using Sitecore.API.Foundation.Authorization.Services;

namespace Sitecore.API.Foundation.Tests;

public class TypedServiceCollectionExtensionsTests
{
    public sealed class XmCloudStagingClient(
        HttpClient httpClient,
        ISitecoreTokenService<XmCloudStagingClient> tokenService)
    {
        public HttpClient HttpClient { get; } = httpClient;
        public ISitecoreTokenService<XmCloudStagingClient> TokenService { get; } = tokenService;
    }

    public sealed class XmCloudProductionClient(
        HttpClient httpClient,
        ISitecoreTokenService<XmCloudProductionClient> tokenService)
    {
        public HttpClient HttpClient { get; } = httpClient;
        public ISitecoreTokenService<XmCloudProductionClient> TokenService { get; } = tokenService;
    }

    public sealed class GenericSitecoreClient<TTenant>(
        ISitecoreTokenService<GenericSitecoreClient<TTenant>> tokenService)
        where TTenant : class
    {
        public ISitecoreTokenService<GenericSitecoreClient<TTenant>> TokenService { get; } = tokenService;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        private int _callCount;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            : this((request, _) => responder(request))
        {
        }

        public int CallCount => _callCount;
        public List<Uri?> RequestedUris { get; } = [];
        public List<string?> CustomHeaderValues { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref _callCount);
            RequestedUris.Add(request.RequestUri);
            request.Headers.TryGetValues("X-Auth-Client", out var values);
            CustomHeaderValues.Add(values != null ? string.Join(",", values) : null);
            return Task.FromResult(responder(request, attempt));
        }

        public static HttpResponseMessage CreateTokenResponse(string accessToken, int expiresIn = 3600)
            => TestHttpResponseHelper.CreateTokenResponse(accessToken, expiresIn);
    }

    [Fact]
    public void AddSitecoreAuthentication_Generic_ShouldRegisterScopedServiceAndSingletonCache()
    {
        var services = new ServiceCollection();

        var builder = services.AddSitecoreAuthentication<XmCloudStagingClient>();
        builder.ShouldNotBeNull();
        builder.Services.ShouldBeSameAs(services);

        using var provider = services.BuildServiceProvider();
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var svc1 = scope1.ServiceProvider.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>();
        var svc1SameScope = scope1.ServiceProvider.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>();
        var svc2 = scope2.ServiceProvider.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>();

        svc1.ShouldBeSameAs(svc1SameScope);
        svc1.ShouldNotBeSameAs(svc2);

        var cache1 = scope1.ServiceProvider.GetRequiredService<ISitecoreTokenCache>();
        var cache2 = scope2.ServiceProvider.GetRequiredService<ISitecoreTokenCache>();
        cache1.ShouldBeSameAs(cache2);
    }

    [Fact]
    public void AddSitecoreAuthenticationSingleton_Generic_ShouldRegisterSingletonService()
    {
        var services = new ServiceCollection();

        services.AddSitecoreAuthenticationSingleton<XmCloudStagingClient>();

        using var provider = services.BuildServiceProvider();
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var svc1 = scope1.ServiceProvider.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>();
        var svc2 = scope2.ServiceProvider.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>();

        svc1.ShouldBeSameAs(svc2);
    }

    [Fact]
    public async Task TypedClients_WithDifferentAuthTokenUrlsAndHttpClients_ShouldIsolateEndpointsAndShareCache()
    {
        var stagingUrl = "https://staging-auth.sitecorecloud.io/oauth/token";
        var prodUrl = "https://prod-auth.sitecorecloud.io/oauth/token";

        var stagingHandler = new RecordingHandler((_, attempt) =>
            RecordingHandler.CreateTokenResponse($"staging-token-{attempt}"));
        var prodHandler = new RecordingHandler((_, attempt) =>
            RecordingHandler.CreateTokenResponse($"prod-token-{attempt}"));

        var services = new ServiceCollection();

        services.AddSitecoreAuthentication<XmCloudStagingClient>(options =>
            {
                options.AuthTokenUrl = stagingUrl;
            })
            .ConfigureHttpClient(b => b
                .ConfigureHttpClient(c => c.DefaultRequestHeaders.Add("X-Auth-Client", "staging"))
                .ConfigurePrimaryHttpMessageHandler(() => stagingHandler));

        services.AddSitecoreAuthenticationSingleton<XmCloudProductionClient>(options =>
            {
                options.AuthTokenUrl = prodUrl;
            })
            .ConfigureHttpClient(b => b
                .ConfigureHttpClient(c => c.DefaultRequestHeaders.Add("X-Auth-Client", "prod"))
                .ConfigurePrimaryHttpMessageHandler(() => prodHandler));

        using var provider = services.BuildServiceProvider();
        var stagingService = provider.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>();
        var prodService = provider.GetRequiredService<ISitecoreTokenService<XmCloudProductionClient>>();
        var sharedCache = provider.GetRequiredService<ISitecoreTokenCache>();

        // Identical credentials across both environments
        var sharedCredentials = new SitecoreAuthClientCredentials("shared-client", "shared-secret");

        var stagingToken1 = await stagingService.GetSitecoreAuthToken(sharedCredentials);
        var prodToken1 = await prodService.GetSitecoreAuthToken(sharedCredentials);

        stagingToken1.AccessToken.ShouldBe("staging-token-1");
        prodToken1.AccessToken.ShouldBe("prod-token-1");
        sharedCache.CacheSize.ShouldBe(2);

        stagingHandler.RequestedUris.ShouldHaveSingleItem().ShouldBe(new Uri(stagingUrl));
        stagingHandler.CustomHeaderValues.ShouldHaveSingleItem().ShouldBe("staging");
        prodHandler.RequestedUris.ShouldHaveSingleItem().ShouldBe(new Uri(prodUrl));
        prodHandler.CustomHeaderValues.ShouldHaveSingleItem().ShouldBe("prod");

        // Second lookup should hit cache for each endpoint without new HTTP calls
        var stagingToken2 = await stagingService.GetSitecoreAuthToken(sharedCredentials);
        var prodToken2 = await prodService.GetSitecoreAuthToken(sharedCredentials);

        stagingToken2.ShouldBe(stagingToken1);
        prodToken2.ShouldBe(prodToken1);
        stagingHandler.CallCount.ShouldBe(1);
        prodHandler.CallCount.ShouldBe(1);

        // Cross-endpoint refresh attempt must fail and must NOT evict the other endpoint's token
        await Should.ThrowAsync<ArgumentException>(() =>
            prodService.TryRefreshSitecoreAuthToken(stagingToken1));
        sharedCache.GetToken(sharedCredentials, stagingUrl).ShouldBe(stagingToken1);

        // Refreshing on the owning service succeeds and only refreshes that endpoint's entry
        var refreshedStaging = await stagingService.TryRefreshSitecoreAuthToken(stagingToken1);
        refreshedStaging.AccessToken.ShouldBe("staging-token-2");
        stagingHandler.CallCount.ShouldBe(2);
        prodHandler.CallCount.ShouldBe(1);
        sharedCache.GetToken(sharedCredentials, prodUrl).ShouldBe(prodToken1);
    }

    [Fact]
    public async Task TypedRegistration_ShouldCoexistWithAddHttpClientForSameConsumerType()
    {
        var authUrl = "https://staging-auth.sitecorecloud.io/oauth/token";
        var apiBaseAddress = new Uri("https://xmcloud-api.sitecorecloud.io/");

        var authHandler = new RecordingHandler((_, _) =>
            RecordingHandler.CreateTokenResponse("coexist-token"));

        var services = new ServiceCollection();

        // Consumer registers its own typed HttpClient for Sitecore API calls
        services.AddHttpClient<XmCloudStagingClient>(client =>
        {
            client.BaseAddress = apiBaseAddress;
            client.DefaultRequestHeaders.Add("X-Api-Client", "xmcloud-api");
        });

        // Consumer also registers typed Sitecore authentication for XmCloudStagingClient
        services.AddSitecoreAuthentication<XmCloudStagingClient>()
            .ConfigureOptions(o => o.AuthTokenUrl = authUrl)
            .ConfigureHttpClient(b => b
                .ConfigureHttpClient(c => c.DefaultRequestHeaders.Add("X-Auth-Client", "sitecore-auth"))
                .ConfigurePrimaryHttpMessageHandler(() => authHandler));

        using var provider = services.BuildServiceProvider();
        var consumer = provider.GetRequiredService<XmCloudStagingClient>();

        consumer.HttpClient.BaseAddress.ShouldBe(apiBaseAddress);
        consumer.HttpClient.DefaultRequestHeaders.Contains("X-Api-Client").ShouldBeTrue();
        consumer.HttpClient.DefaultRequestHeaders.Contains("X-Auth-Client").ShouldBeFalse();

        var token = await consumer.TokenService.GetSitecoreAuthToken(
            new SitecoreAuthClientCredentials("client-1", "secret-1"));
        token.AccessToken.ShouldBe("coexist-token");
        authHandler.CallCount.ShouldBe(1);
        authHandler.CustomHeaderValues.ShouldHaveSingleItem().ShouldBe("sitecore-auth");
    }

    [Fact]
    public async Task AddSitecoreAuthentication_Generic_WithIConfiguration_ShouldBindNamedSection()
    {
        var configData = new Dictionary<string, string?>
        {
            ["SitecoreAuth:Staging:AuthTokenUrl"] = "https://config-staging.sitecorecloud.io/oauth/token",
            ["SitecoreAuth:Prod:AuthTokenUrl"] = "https://config-prod.sitecorecloud.io/oauth/token"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var stagingHandler = new RecordingHandler((_, _) => RecordingHandler.CreateTokenResponse("cfg-staging"));
        var prodHandler = new RecordingHandler((_, _) => RecordingHandler.CreateTokenResponse("cfg-prod"));

        var services = new ServiceCollection();
        services.AddSitecoreAuthentication<XmCloudStagingClient>(configuration, "SitecoreAuth:Staging")
            .ConfigureHttpClient(b => b.ConfigurePrimaryHttpMessageHandler(() => stagingHandler));

        services.AddSitecoreAuthenticationSingleton<XmCloudProductionClient>(configuration, "SitecoreAuth:Prod")
            .ConfigureHttpClient(b => b.ConfigurePrimaryHttpMessageHandler(() => prodHandler));

        using var provider = services.BuildServiceProvider();
        var stagingSvc = provider.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>();
        var prodSvc = provider.GetRequiredService<ISitecoreTokenService<XmCloudProductionClient>>();

        var creds = new SitecoreAuthClientCredentials("cfg-id", "cfg-secret");
        var t1 = await stagingSvc.GetSitecoreAuthToken(creds);
        var t2 = await prodSvc.GetSitecoreAuthToken(creds);

        t1.AccessToken.ShouldBe("cfg-staging");
        t2.AccessToken.ShouldBe("cfg-prod");
        stagingHandler.RequestedUris.ShouldHaveSingleItem().ShouldBe(new Uri("https://config-staging.sitecorecloud.io/oauth/token"));
        prodHandler.RequestedUris.ShouldHaveSingleItem().ShouldBe(new Uri("https://config-prod.sitecorecloud.io/oauth/token"));
    }

    [Fact]
    public async Task ConfigureResilience_CalledMultipleTimes_ShouldReconfigureSinglePipelineWithoutStackingRetries()
    {
        // Always return 500 with MaxRetryAttempts = 1.
        // A single pipeline performs 1 initial attempt + 1 retry = 2 calls; stacked duplicate
        // pipelines would multiply attempts (4 calls).
        using var handler = new RecordingHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var services = new ServiceCollection();
        services.AddSitecoreAuthentication<XmCloudStagingClient>(o =>
            {
                o.AuthTokenUrl = "https://resilient-auth.sitecorecloud.io/oauth/token";
            })
            .ConfigureResilience()
            .ConfigureResilience(resilience =>
            {
                resilience.Retry.MaxRetryAttempts = 1;
                resilience.Retry.Delay = TimeSpan.FromMilliseconds(1);
                resilience.Retry.UseJitter = false;
            })
            .ConfigureHttpClient(b => b.ConfigurePrimaryHttpMessageHandler(() => handler));

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>();

        var ex = await Should.ThrowAsync<SitecoreAuthHttpException>(
            () => service.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("res-client", "res-secret")));
        ex.StatusCode.ShouldBe((int)HttpStatusCode.InternalServerError);
        handler.CallCount.ShouldBe(2);
    }

    [Fact]
    public async Task UseHttpClientFactory_ShouldOverrideNamedHttpClientCreation()
    {
        var customHandler = new RecordingHandler((_, _) => RecordingHandler.CreateTokenResponse("factory-token"));
        using var customHttpClient = new HttpClient(customHandler);
        var factoryInvoked = false;

        var services = new ServiceCollection();
        services.AddSitecoreAuthentication<XmCloudStagingClient>(o =>
            {
                o.AuthTokenUrl = "https://factory-auth.sitecorecloud.io/oauth/token";
            })
            .UseHttpClientFactory(_ =>
            {
                factoryInvoked = true;
                return customHttpClient;
            });

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>();

        var token = await service.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("f-client", "f-secret"));
        token.AccessToken.ShouldBe("factory-token");
        factoryInvoked.ShouldBeTrue();
        customHandler.CallCount.ShouldBe(1);
    }

    [Fact]
    public void AddSitecoreAuthentication_Generic_ShouldRespectPreRegisteredTypedTokenService()
    {
        var customImplementation = Substitute.For<ISitecoreTokenService<XmCloudStagingClient>>();
        var services = new ServiceCollection();
        services.TryAddSingleton(customImplementation);

        services.AddSitecoreAuthentication<XmCloudStagingClient>();

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>();
        resolved.ShouldBeSameAs(customImplementation);
    }

    [Fact]
    public void BuilderMethods_WithInvalidArguments_ShouldThrowExpectedExceptions()
    {
        IServiceCollection? nullServices = null;
        var config = new ConfigurationBuilder().Build();
        Should.Throw<ArgumentNullException>(() => nullServices!.AddSitecoreAuthentication<XmCloudStagingClient>())
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() => nullServices!.AddSitecoreAuthenticationSingleton<XmCloudStagingClient>())
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() => nullServices!.AddSitecoreAuthentication<XmCloudStagingClient>(config))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() => nullServices!.AddSitecoreAuthenticationSingleton<XmCloudStagingClient>(config))
            .ParamName.ShouldBe("services");

        var services = new ServiceCollection();
        Should.Throw<ArgumentNullException>(() =>
            services.AddSitecoreAuthentication<XmCloudStagingClient>((IConfiguration)null!))
            .ParamName.ShouldBe("configuration");
        services.Count.ShouldBe(0);

        Should.Throw<ArgumentNullException>(() =>
            services.AddSitecoreAuthentication<XmCloudStagingClient>(config, (string)null!))
            .ParamName.ShouldBe("sectionName");
        Should.Throw<ArgumentException>(() =>
            services.AddSitecoreAuthentication<XmCloudStagingClient>(config, ""))
            .ParamName.ShouldBe("sectionName");
        Should.Throw<ArgumentException>(() =>
            services.AddSitecoreAuthentication<XmCloudStagingClient>(config, "   "))
            .ParamName.ShouldBe("sectionName");
        services.Count.ShouldBe(0);

        Should.Throw<ArgumentNullException>(() =>
            services.AddSitecoreAuthenticationSingleton<XmCloudStagingClient>((IConfiguration)null!))
            .ParamName.ShouldBe("configuration");
        services.Count.ShouldBe(0);

        Should.Throw<ArgumentNullException>(() =>
            services.AddSitecoreAuthenticationSingleton<XmCloudStagingClient>(config, (string)null!))
            .ParamName.ShouldBe("sectionName");
        Should.Throw<ArgumentException>(() =>
            services.AddSitecoreAuthenticationSingleton<XmCloudStagingClient>(config, ""))
            .ParamName.ShouldBe("sectionName");
        Should.Throw<ArgumentException>(() =>
            services.AddSitecoreAuthenticationSingleton<XmCloudStagingClient>(config, "   "))
            .ParamName.ShouldBe("sectionName");
        services.Count.ShouldBe(0);

        var builder = services.AddSitecoreAuthentication<XmCloudStagingClient>();

        Should.Throw<ArgumentNullException>(() =>
            builder.ConfigureOptions((Action<SitecoreTokenServiceOptions>)null!))
            .ParamName.ShouldBe("configureOptions");
        Should.Throw<ArgumentNullException>(() =>
            builder.ConfigureOptions((IConfiguration)null!))
            .ParamName.ShouldBe("configuration");
        Should.Throw<ArgumentNullException>(() =>
            builder.ConfigureOptions(config, (string)null!))
            .ParamName.ShouldBe("sectionName");
        Should.Throw<ArgumentException>(() =>
            builder.ConfigureOptions(config, ""))
            .ParamName.ShouldBe("sectionName");
        Should.Throw<ArgumentException>(() =>
            builder.ConfigureOptions(config, "   "))
            .ParamName.ShouldBe("sectionName");
        Should.Throw<ArgumentNullException>(() =>
            builder.ConfigureHttpClient(null!))
            .ParamName.ShouldBe("configureClient");
        Should.Throw<ArgumentNullException>(() =>
            builder.UseHttpClientFactory(null!))
            .ParamName.ShouldBe("httpClientFactory");
    }

    [Fact]
    public async Task RepeatedAddSitecoreAuthentication_ForSameType_ShouldReuseBuilderAndHonorSubsequentConfiguration()
    {
        var customHandler = new RecordingHandler((_, _) => RecordingHandler.CreateTokenResponse("second-builder-token"));
        using var customHttpClient = new HttpClient(customHandler);

        var services = new ServiceCollection();
        var builder1 = services.AddSitecoreAuthentication<XmCloudStagingClient>(o =>
            {
                o.AuthTokenUrl = "https://initial-auth.sitecorecloud.io/oauth/token";
            })
            .ConfigureResilience();

        var builder2 = services.AddSitecoreAuthentication<XmCloudStagingClient>(o =>
            {
                o.AuthTokenUrl = "https://override-auth.sitecorecloud.io/oauth/token";
            })
            .ConfigureResilience(r => r.Retry.MaxRetryAttempts = 1)
            .UseHttpClientFactory(_ => customHttpClient);

        builder2.ShouldBeSameAs(builder1);

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>();

        var token = await service.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("repeat-id", "repeat-secret"));
        token.AccessToken.ShouldBe("second-builder-token");
        customHandler.RequestedUris.ShouldHaveSingleItem()
            .ShouldBe(new Uri("https://override-auth.sitecorecloud.io/oauth/token"));
    }

    [Fact]
    public async Task ClosedConstructedGenericTClientTypes_ShouldIsolateOptionsAndEndpointsByFullName()
    {
        var stagingUrl = "https://generic-staging.sitecorecloud.io/oauth/token";
        var prodUrl = "https://generic-prod.sitecorecloud.io/oauth/token";

        var stagingHandler = new RecordingHandler((_, _) => RecordingHandler.CreateTokenResponse("generic-staging-token"));
        var prodHandler = new RecordingHandler((_, _) => RecordingHandler.CreateTokenResponse("generic-prod-token"));

        var services = new ServiceCollection();
        services.AddSitecoreAuthentication<GenericSitecoreClient<XmCloudStagingClient>>(o => o.AuthTokenUrl = stagingUrl)
            .ConfigureHttpClient(b => b.ConfigurePrimaryHttpMessageHandler(() => stagingHandler));

        services.AddSitecoreAuthentication<GenericSitecoreClient<XmCloudProductionClient>>(o => o.AuthTokenUrl = prodUrl)
            .ConfigureHttpClient(b => b.ConfigurePrimaryHttpMessageHandler(() => prodHandler));

        using var provider = services.BuildServiceProvider();
        var stagingSvc = provider.GetRequiredService<ISitecoreTokenService<GenericSitecoreClient<XmCloudStagingClient>>>();
        var prodSvc = provider.GetRequiredService<ISitecoreTokenService<GenericSitecoreClient<XmCloudProductionClient>>>();

        var creds = new SitecoreAuthClientCredentials("gen-client", "gen-secret");
        var stagingToken = await stagingSvc.GetSitecoreAuthToken(creds);
        var prodToken = await prodSvc.GetSitecoreAuthToken(creds);

        stagingToken.AccessToken.ShouldBe("generic-staging-token");
        prodToken.AccessToken.ShouldBe("generic-prod-token");
        stagingHandler.RequestedUris.ShouldHaveSingleItem().ShouldBe(new Uri(stagingUrl));
        prodHandler.RequestedUris.ShouldHaveSingleItem().ShouldBe(new Uri(prodUrl));
    }

    [Fact]
    public async Task TypedClients_WithCustomEndpointAwareTokenCache_ShouldMaintainStrictEndpointIsolationWithoutFallbackLeakage()
    {
        var stagingUrl = "https://custom-staging.sitecorecloud.io/oauth/token";
        var prodUrl = "https://custom-prod.sitecorecloud.io/oauth/token";

        var stagingHandler = new RecordingHandler((_, attempt) =>
            RecordingHandler.CreateTokenResponse($"custom-staging-token-{attempt}"));
        var prodHandler = new RecordingHandler((_, attempt) =>
            RecordingHandler.CreateTokenResponse($"custom-prod-token-{attempt}"));

        var customCache = new CustomEndpointAwareTokenCache();
        var services = new ServiceCollection();
        services.AddSingleton<ISitecoreTokenCache>(customCache);

        services.AddSitecoreAuthentication<XmCloudStagingClient>(o => o.AuthTokenUrl = stagingUrl)
            .ConfigureHttpClient(b => b.ConfigurePrimaryHttpMessageHandler(() => stagingHandler));

        services.AddSitecoreAuthentication<XmCloudProductionClient>(o => o.AuthTokenUrl = prodUrl)
            .ConfigureHttpClient(b => b.ConfigurePrimaryHttpMessageHandler(() => prodHandler));

        using var provider = services.BuildServiceProvider();
        var stagingSvc = provider.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>();
        var prodSvc = provider.GetRequiredService<ISitecoreTokenService<XmCloudProductionClient>>();

        var sharedCreds = new SitecoreAuthClientCredentials("shared-custom-id", "shared-custom-secret");

        // Acquiring staging token must NOT leak into production lookup via 1-arg/2-arg fallback
        var stagingToken = await stagingSvc.GetSitecoreAuthToken(sharedCreds);
        var prodToken = await prodSvc.GetSitecoreAuthToken(sharedCreds);

        stagingToken.AccessToken.ShouldBe("custom-staging-token-1");
        prodToken.AccessToken.ShouldBe("custom-prod-token-1");
        stagingHandler.CallCount.ShouldBe(1);
        prodHandler.CallCount.ShouldBe(1);

        // Cross-endpoint refresh must NOT fall back to 1-arg RemoveToken(token) and evict staging's token
        await Should.ThrowAsync<ArgumentException>(() =>
            prodSvc.TryRefreshSitecoreAuthToken(stagingToken));
        customCache.GetToken(sharedCreds, stagingUrl).ShouldBe(stagingToken);

        // Refreshing on the owning staging service should succeed
        var refreshedStaging = await stagingSvc.TryRefreshSitecoreAuthToken(stagingToken);
        refreshedStaging.AccessToken.ShouldBe("custom-staging-token-2");
        customCache.GetToken(sharedCreds, prodUrl).ShouldBe(prodToken);
    }

    [Fact]
    public async Task TypedTokenService_ShouldLogToTypedCategoryLoggerOnAcquisitionAndCacheHit()
    {
        var stagingUrl = "https://logged-staging.sitecorecloud.io/oauth/token";
        var handler = new RecordingHandler((_, _) =>
            RecordingHandler.CreateTokenResponse("logged-staging-token"));
        var typedLogger = Substitute.For<ILogger<SitecoreTokenService<XmCloudStagingClient>>>();

        var services = new ServiceCollection();
        services.AddSingleton(typedLogger);
        services.AddSitecoreAuthentication<XmCloudStagingClient>(o => o.AuthTokenUrl = stagingUrl)
            .ConfigureHttpClient(b => b.ConfigurePrimaryHttpMessageHandler(() => handler));

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>();
        var creds = new SitecoreAuthClientCredentials("log-client", "log-secret");

        // 1. Cache miss -> HTTP token acquisition
        var token1 = await service.GetSitecoreAuthToken(creds);
        token1.AccessToken.ShouldBe("logged-staging-token");

        typedLogger.ShouldHaveLogged(
            LogLevel.Information,
            msg => msg.Contains("Requesting new token") && msg.Contains("log-client"));
        typedLogger.ShouldHaveLogged(
            LogLevel.Information,
            msg => msg.Contains("Token acquired and cached") && msg.Contains("log-client"));

        // 2. Cache hit -> logs cache hit without making a second HTTP call
        var token2 = await service.GetSitecoreAuthToken(creds);
        token2.ShouldBe(token1);
        handler.CallCount.ShouldBe(1);

        typedLogger.ShouldHaveLogged(
            LogLevel.Information,
            msg => msg.Contains("Token cache hit") && msg.Contains("log-client"));
    }

    [Fact]
    public async Task UntypedAndTypedRegistrations_ShouldCoexistShareSingletonCacheAndHonorGlobalMaxCacheSize()
    {
        var defaultUrl = "https://default-auth.sitecorecloud.io/oauth/token";
        var stagingUrl = "https://staging-auth.sitecorecloud.io/oauth/token";
        var prodUrl = "https://prod-auth.sitecorecloud.io/oauth/token";

        var defaultHandler = new RecordingHandler((_, _) =>
            RecordingHandler.CreateTokenResponse("default-token", expiresIn: 600));
        var stagingHandler = new RecordingHandler((_, _) =>
            RecordingHandler.CreateTokenResponse("staging-token", expiresIn: 1200));
        var prodHandler = new RecordingHandler((_, _) =>
            RecordingHandler.CreateTokenResponse("prod-token", expiresIn: 1800));

        using var defaultHttpClient = new HttpClient(defaultHandler);

        var services = new ServiceCollection();

        // Untyped registration configures DefaultName options (including global MaxCacheSize = 2)
        services.AddSitecoreAuthentication(
            configureOptions: options =>
            {
                options.AuthTokenUrl = defaultUrl;
                options.MaxCacheSize = 2;
            },
            httpClientFactory: _ => defaultHttpClient);

        services.AddSitecoreAuthentication<XmCloudStagingClient>(o => o.AuthTokenUrl = stagingUrl)
            .ConfigureHttpClient(b => b.ConfigurePrimaryHttpMessageHandler(() => stagingHandler));

        services.AddSitecoreAuthentication<XmCloudProductionClient>(o => o.AuthTokenUrl = prodUrl)
            .ConfigureHttpClient(b => b.ConfigurePrimaryHttpMessageHandler(() => prodHandler));

        using var provider = services.BuildServiceProvider();
        var untypedSvc = provider.GetRequiredService<ISitecoreTokenService>();
        var stagingSvc = provider.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>();
        var prodSvc = provider.GetRequiredService<ISitecoreTokenService<XmCloudProductionClient>>();
        var sharedCache = provider.GetRequiredService<ISitecoreTokenCache>();

        var sharedCreds = new SitecoreAuthClientCredentials("coexist-client", "coexist-secret");

        var defaultToken = await untypedSvc.GetSitecoreAuthToken(sharedCreds);
        var stagingToken = await stagingSvc.GetSitecoreAuthToken(sharedCreds);

        defaultToken.AccessToken.ShouldBe("default-token");
        stagingToken.AccessToken.ShouldBe("staging-token");
        sharedCache.CacheSize.ShouldBe(2);

        // Acquiring a 3rd token via prodSvc exceeds global MaxCacheSize = 2 and evicts the earliest-expiring token (defaultToken)
        var prodToken = await prodSvc.GetSitecoreAuthToken(sharedCreds);
        prodToken.AccessToken.ShouldBe("prod-token");

        defaultHandler.RequestedUris.ShouldHaveSingleItem().ShouldBe(new Uri(defaultUrl));
        stagingHandler.RequestedUris.ShouldHaveSingleItem().ShouldBe(new Uri(stagingUrl));
        prodHandler.RequestedUris.ShouldHaveSingleItem().ShouldBe(new Uri(prodUrl));

        sharedCache.CacheSize.ShouldBe(2);
        sharedCache.GetToken(sharedCreds, defaultUrl).ShouldBeNull();
        sharedCache.GetToken(sharedCreds, stagingUrl).ShouldBe(stagingToken);
        sharedCache.GetToken(sharedCreds, prodUrl).ShouldBe(prodToken);
    }

    [Fact]
    public void MixedLifetimeRepeatedRegistration_ShouldReuseBuilderKeepFirstLifetimeAndNotLeakBuilderIntoDI()
    {
        var scopedFirst = new ServiceCollection();
        var sb1 = scopedFirst.AddSitecoreAuthentication<XmCloudStagingClient>();
        var sb2 = scopedFirst.AddSitecoreAuthenticationSingleton<XmCloudStagingClient>();
        sb2.ShouldBeSameAs(sb1);
        scopedFirst.Any(d => d.ServiceType.Name.StartsWith("SitecoreAuthenticationBuilder", StringComparison.Ordinal))
            .ShouldBeFalse();

        using (var sp = scopedFirst.BuildServiceProvider())
        {
            using var s1 = sp.CreateScope();
            using var s2 = sp.CreateScope();
            s1.ServiceProvider.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>()
                .ShouldNotBeSameAs(s2.ServiceProvider.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>());
        }

        var singletonFirst = new ServiceCollection();
        var tb1 = singletonFirst.AddSitecoreAuthenticationSingleton<XmCloudProductionClient>();
        var tb2 = singletonFirst.AddSitecoreAuthentication<XmCloudProductionClient>();
        tb2.ShouldBeSameAs(tb1);

        using var singletonSp = singletonFirst.BuildServiceProvider();
        using var scopeA = singletonSp.CreateScope();
        using var scopeB = singletonSp.CreateScope();
        scopeA.ServiceProvider.GetRequiredService<ISitecoreTokenService<XmCloudProductionClient>>()
            .ShouldBeSameAs(scopeB.ServiceProvider.GetRequiredService<ISitecoreTokenService<XmCloudProductionClient>>());
    }

    [Fact]
    public async Task BuilderConfigureOptions_ChainedWithIConfigurationAndActionInBothOrders_ShouldApplyInOrder()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SitecoreAuthentication:AuthTokenUrl"] = "https://default-section.sitecorecloud.io/oauth/token",
                ["SectionA:AuthTokenUrl"] = "https://section-a.sitecorecloud.io/oauth/token"
            })
            .Build();

        using var stagingHandler = new RecordingHandler(_ => RecordingHandler.CreateTokenResponse("a-overridden"));
        using var prodHandler = new RecordingHandler(_ => RecordingHandler.CreateTokenResponse("default-cfg"));

        var services = new ServiceCollection();

        // Order 1: IConfiguration first, then Action override
        services.AddSitecoreAuthentication<XmCloudStagingClient>()
            .ConfigureOptions(configuration, "SectionA")
            .ConfigureOptions(o => o.AuthTokenUrl = "https://action-override.sitecorecloud.io/oauth/token")
            .ConfigureHttpClient(b => b.ConfigurePrimaryHttpMessageHandler(() => stagingHandler));

        // Order 2: Action first, then builder.ConfigureOptions(configuration) with default section name
        services.AddSitecoreAuthenticationSingleton<XmCloudProductionClient>()
            .ConfigureOptions(o => o.AuthTokenUrl = "https://initial-action.sitecorecloud.io/oauth/token")
            .ConfigureOptions(configuration)
            .ConfigureHttpClient(b => b.ConfigurePrimaryHttpMessageHandler(() => prodHandler));

        using var provider = services.BuildServiceProvider();
        var stagingSvc = provider.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>();
        var prodSvc = provider.GetRequiredService<ISitecoreTokenService<XmCloudProductionClient>>();

        await stagingSvc.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("c1", "s1"));
        await prodSvc.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("c2", "s2"));

        stagingHandler.RequestedUris.ShouldHaveSingleItem()
            .ShouldBe(new Uri("https://action-override.sitecorecloud.io/oauth/token"));
        prodHandler.RequestedUris.ShouldHaveSingleItem()
            .ShouldBe(new Uri("https://default-section.sitecorecloud.io/oauth/token"));
    }

    [Fact]
    public async Task AddSitecoreAuthenticationSingleton_Generic_WithDefaultSectionIConfiguration_ShouldBindAndBeSingleton()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SitecoreAuthentication:AuthTokenUrl"] = "https://default-singleton.sitecorecloud.io/oauth/token"
            })
            .Build();

        using var handler = new RecordingHandler(_ => RecordingHandler.CreateTokenResponse("default-section-singleton"));
        var services = new ServiceCollection();
        services.AddSitecoreAuthenticationSingleton<XmCloudStagingClient>(configuration)
            .ConfigureHttpClient(b => b.ConfigurePrimaryHttpMessageHandler(() => handler));

        using var provider = services.BuildServiceProvider();
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();
        var svc1 = scope1.ServiceProvider.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>();
        var svc2 = scope2.ServiceProvider.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>();

        svc1.ShouldBeSameAs(svc2);
        var tok = await svc1.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("id", "secret"));
        tok.AccessToken.ShouldBe("default-section-singleton");
        handler.RequestedUris.ShouldHaveSingleItem()
            .ShouldBe(new Uri("https://default-singleton.sitecorecloud.io/oauth/token"));
    }

    [Fact]
    public async Task UseHttpClientFactory_PerResolveLifetimeAndOverride_ShouldHonorScopeAndLastFactory()
    {
        using var ignoredHandler = new RecordingHandler(_ => RecordingHandler.CreateTokenResponse("ignored"));
        using var activeHandler = new RecordingHandler((_, attempt) => RecordingHandler.CreateTokenResponse($"active-{attempt}"));
        using var ignoredClient = new HttpClient(ignoredHandler);
        using var client1 = new HttpClient(activeHandler);
        using var client2 = new HttpClient(activeHandler);

        var scopedFactoryProviders = new List<IServiceProvider>();
        var services = new ServiceCollection();

        services.AddSitecoreAuthentication<XmCloudStagingClient>(o =>
                o.AuthTokenUrl = "https://scoped-factory.sitecorecloud.io/oauth/token")
            .UseHttpClientFactory(_ => ignoredClient)
            .UseHttpClientFactory(sp =>
            {
                scopedFactoryProviders.Add(sp);
                return scopedFactoryProviders.Count == 1 ? client1 : client2;
            });

        var singletonFactoryCalls = 0;
        services.AddSitecoreAuthenticationSingleton<XmCloudProductionClient>(o =>
                o.AuthTokenUrl = "https://singleton-factory.sitecorecloud.io/oauth/token")
            .UseHttpClientFactory(_ =>
            {
                singletonFactoryCalls++;
                return client1;
            });

        using var provider = services.BuildServiceProvider();
        using (var scope1 = provider.CreateScope())
        using (var scope2 = provider.CreateScope())
        {
            var scopedSvc1 = scope1.ServiceProvider.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>();
            var scopedSvc2 = scope2.ServiceProvider.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>();
            _ = scope1.ServiceProvider.GetRequiredService<ISitecoreTokenService<XmCloudProductionClient>>();
            _ = scope2.ServiceProvider.GetRequiredService<ISitecoreTokenService<XmCloudProductionClient>>();

            var t1 = await scopedSvc1.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("c1", "s1"));
            var t2 = await scopedSvc2.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("c2", "s2"));

            t1.AccessToken.ShouldBe("active-1");
            t2.AccessToken.ShouldBe("active-2");
            scopedFactoryProviders.Count.ShouldBe(2);
            scopedFactoryProviders[0].ShouldBeSameAs(scope1.ServiceProvider);
            scopedFactoryProviders[1].ShouldBeSameAs(scope2.ServiceProvider);
            ignoredHandler.CallCount.ShouldBe(0);
            singletonFactoryCalls.ShouldBe(1);
        }
    }

    [Fact]
    public async Task TypedRegistration_IsolationAndGlobalCacheSettings_ShouldNotLeakUntypedOrOverrideMaxCacheSize()
    {
        using var handler = new RecordingHandler((_, attempt) => RecordingHandler.CreateTokenResponse($"iso-{attempt}"));

        // Typed-only registration should not register untyped ISitecoreTokenService
        var typedOnlyServices = new ServiceCollection();
        typedOnlyServices.AddSitecoreAuthentication<XmCloudStagingClient>(o =>
            {
                o.AuthTokenUrl = "https://typed-only.sitecorecloud.io/oauth/token";
                o.MaxCacheSize = 1; // Ignored by singleton cache (which reads Options.DefaultName MaxCacheSize = 10)
            })
            .ConfigureHttpClient(b => b.ConfigurePrimaryHttpMessageHandler(() => handler));

        using (var typedOnlySp = typedOnlyServices.BuildServiceProvider())
        {
            typedOnlySp.GetService<ISitecoreTokenService>().ShouldBeNull();
            var typedSvc = typedOnlySp.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>();
            var cache = typedOnlySp.GetRequiredService<ISitecoreTokenCache>();

            await typedSvc.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("k1", "s1"));
            await typedSvc.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("k2", "s2"));
            cache.CacheSize.ShouldBe(2); // Proves named MaxCacheSize=1 did not override global MaxCacheSize=10
        }

        // Untyped AuthTokenUrl should not leak into typed service registered without custom AuthTokenUrl
        using var untypedHandler = new RecordingHandler(_ => RecordingHandler.CreateTokenResponse("untyped"));
        using var defaultTypedHandler = new RecordingHandler(_ => RecordingHandler.CreateTokenResponse("default-typed"));
        var mixedServices = new ServiceCollection();
        mixedServices.AddSitecoreAuthentication(
            o => o.AuthTokenUrl = "https://custom-untyped.sitecorecloud.io/oauth/token",
            configureClient: b => b.ConfigurePrimaryHttpMessageHandler(() => untypedHandler));
        mixedServices.AddSitecoreAuthentication<XmCloudStagingClient>()
            .ConfigureHttpClient(b => b.ConfigurePrimaryHttpMessageHandler(() => defaultTypedHandler));

        using var mixedSp = mixedServices.BuildServiceProvider();
        var typedWithDefault = mixedSp.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>();
        await typedWithDefault.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("u1", "p1"));

        defaultTypedHandler.RequestedUris.ShouldHaveSingleItem()
            .ShouldBe(new Uri("https://auth.sitecorecloud.io/oauth/token"));
    }

    [Fact]
    public async Task ConfigureHttpClient_AndConfigureResilience_InEitherOrder_ShouldPreserveHeadersAndSkipNonTransientRetries()
    {
        using var handlerA = new RecordingHandler((_, attempt) =>
            attempt == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : RecordingHandler.CreateTokenResponse("ordered-a"));
        using var handlerB = new RecordingHandler((_, attempt) =>
            attempt == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : RecordingHandler.CreateTokenResponse("ordered-b"));

        var services = new ServiceCollection();

        // Client config before default ConfigureResilience() (null action)
        services.AddSitecoreAuthentication<XmCloudStagingClient>(o =>
                o.AuthTokenUrl = "https://order-a.sitecorecloud.io/oauth/token")
            .ConfigureHttpClient(b => b
                .ConfigureHttpClient(c => c.DefaultRequestHeaders.Add("X-Auth-Client", "order-a"))
                .ConfigurePrimaryHttpMessageHandler(() => handlerA))
            .ConfigureResilience();

        // ConfigureResilience before ConfigureHttpClient
        services.AddSitecoreAuthentication<XmCloudProductionClient>(o =>
                o.AuthTokenUrl = "https://order-b.sitecorecloud.io/oauth/token")
            .ConfigureResilience(r =>
            {
                r.Retry.MaxRetryAttempts = 2;
                r.Retry.Delay = TimeSpan.FromMilliseconds(1);
                r.Retry.UseJitter = false;
            })
            .ConfigureHttpClient(b => b
                .ConfigureHttpClient(c => c.DefaultRequestHeaders.Add("X-Auth-Client", "order-b"))
                .ConfigurePrimaryHttpMessageHandler(() => handlerB));

        using var sp = services.BuildServiceProvider();
        var svcA = sp.GetRequiredService<ISitecoreTokenService<XmCloudStagingClient>>();
        var svcB = sp.GetRequiredService<ISitecoreTokenService<XmCloudProductionClient>>();

        (await svcA.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("a", "s"))).AccessToken.ShouldBe("ordered-a");
        (await svcB.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("b", "s"))).AccessToken.ShouldBe("ordered-b");

        handlerA.CustomHeaderValues.ShouldAllBe(v => v == "order-a");
        handlerA.CallCount.ShouldBe(2);
        handlerB.CustomHeaderValues.ShouldAllBe(v => v == "order-b");
        handlerB.CallCount.ShouldBe(2);

        // Non-transient 400 should not be retried
        using var badRequestHandler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var nonRetryServices = new ServiceCollection();
        nonRetryServices.AddSitecoreAuthentication<GenericSitecoreClient<XmCloudStagingClient>>(o =>
                o.AuthTokenUrl = "https://non-retry.sitecorecloud.io/oauth/token")
            .ConfigureResilience()
            .ConfigureHttpClient(b => b.ConfigurePrimaryHttpMessageHandler(() => badRequestHandler));

        using var nonRetrySp = nonRetryServices.BuildServiceProvider();
        var ex = await Should.ThrowAsync<SitecoreAuthHttpException>(() =>
            nonRetrySp.GetRequiredService<ISitecoreTokenService<GenericSitecoreClient<XmCloudStagingClient>>>()
                .GetSitecoreAuthToken(new SitecoreAuthClientCredentials("nr", "s")));
        ex.StatusCode.ShouldBe(400);
        badRequestHandler.CallCount.ShouldBe(1);
    }

    private sealed class CustomEndpointAwareTokenCache : ISitecoreTokenCache
    {
        private const string DefaultEndpoint = "https://default.sitecorecloud.io/oauth/token";
        private readonly Dictionary<(SitecoreAuthClientCredentials Credentials, string Url), SitecoreAuthToken> _store = new();

        public int CacheSize => _store.Count;

        public SitecoreAuthToken? GetToken(SitecoreAuthClientCredentials credentials)
            => GetToken(credentials, DefaultEndpoint);

        public SitecoreAuthToken? GetToken(SitecoreAuthClientCredentials credentials, string authTokenUrl)
            => _store.TryGetValue((credentials, authTokenUrl), out var token) && !token.IsExpired ? token : null;

        public void SetToken(SitecoreAuthClientCredentials credentials, SitecoreAuthToken token)
            => SetToken(credentials, token, DefaultEndpoint);

        public void SetToken(SitecoreAuthClientCredentials credentials, SitecoreAuthToken token, string authTokenUrl)
            => _store[(credentials, authTokenUrl)] = token;

        public SitecoreAuthClientCredentials? RemoveToken(SitecoreAuthToken token)
        {
            foreach (var kvp in _store)
            {
                if (kvp.Value.Equals(token))
                {
                    _store.Remove(kvp.Key);
                    return kvp.Key.Credentials;
                }
            }

            return null;
        }

        public SitecoreAuthClientCredentials? RemoveToken(SitecoreAuthToken token, string authTokenUrl)
        {
            foreach (var kvp in _store)
            {
                if (kvp.Value.Equals(token) && string.Equals(kvp.Key.Url, authTokenUrl, StringComparison.Ordinal))
                {
                    _store.Remove(kvp.Key);
                    return kvp.Key.Credentials;
                }
            }

            return null;
        }

        public void ClearCache() => _store.Clear();

        public void PerformCleanup()
        {
        }

        public void Dispose()
        {
        }
    }
}
