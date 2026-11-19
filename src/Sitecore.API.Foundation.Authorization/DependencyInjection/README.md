# DependencyInjection

## Overview

`IServiceCollection` extension methods that register the token cache, the
token service, a named `HttpClient`, optional standard HTTP resilience, and
`SitecoreTokenServiceOptions`. This is the supported entry point for hosting
the library in a `Microsoft.Extensions.DependencyInjection` container.

## Interface

`public static class ServiceCollectionExtensions` — `[ServiceCollectionExtensions.cs:ServiceCollectionExtensions]`

| Method | Service lifetime | Options source |
|---|---|---|
| `AddSitecoreAuthentication(Action<SitecoreTokenServiceOptions>? configureOptions)` | `Scoped` | delegate |
| `AddSitecoreAuthentication(configureOptions = null, configureResilience = null, configureClient = null, httpClientFactory = null)` | `Scoped` | delegate |
| `AddSitecoreAuthentication(IConfiguration configuration, string sectionName)` | `Scoped` | configuration section |
| `AddSitecoreAuthentication(IConfiguration configuration, sectionName = "SitecoreAuthentication", configureResilience = null, configureClient = null, httpClientFactory = null)` | `Scoped` | configuration section |
| `AddSitecoreAuthenticationSingleton(Action<SitecoreTokenServiceOptions>? configureOptions)` | `Singleton` | delegate |
| `AddSitecoreAuthenticationSingleton(configureOptions = null, configureResilience = null, configureClient = null, httpClientFactory = null)` | `Singleton` | delegate |
| `AddSitecoreAuthenticationSingleton(IConfiguration configuration, string sectionName)` | `Singleton` | configuration section |
| `AddSitecoreAuthenticationSingleton(IConfiguration configuration, sectionName = "SitecoreAuthentication", configureResilience = null, configureClient = null, httpClientFactory = null)` | `Singleton` | configuration section |
| `AddSitecoreAuthentication<TClient>(Action<SitecoreTokenServiceOptions>? configureOptions = null)` | `Scoped` | named options per `TClient` (returns `ISitecoreAuthenticationBuilder<TClient>`) |
| `AddSitecoreAuthentication<TClient>(IConfiguration configuration, string sectionName = "SitecoreAuthentication")` | `Scoped` | named configuration section per `TClient` (returns `ISitecoreAuthenticationBuilder<TClient>`) |
| `AddSitecoreAuthenticationSingleton<TClient>(Action<SitecoreTokenServiceOptions>? configureOptions = null)` | `Singleton` | named options per `TClient` (returns `ISitecoreAuthenticationBuilder<TClient>`) |
| `AddSitecoreAuthenticationSingleton<TClient>(IConfiguration configuration, string sectionName = "SitecoreAuthentication")` | `Singleton` | named configuration section per `TClient` (returns `ISitecoreAuthenticationBuilder<TClient>`) |

Optional parameter types: `Action<HttpStandardResilienceOptions>? configureResilience`,
`Action<IHttpClientBuilder>? configureClient`,
`Func<IServiceProvider, HttpClient>? httpClientFactory`.

`public interface ISitecoreAuthenticationBuilder<TClient> where TClient : class` (implemented by `internal sealed class SitecoreAuthenticationBuilder<TClient>`) — `[ISitecoreAuthenticationBuilder.cs:ISitecoreAuthenticationBuilder<TClient>]`
- `IServiceCollection Services { get; }`
- `ISitecoreAuthenticationBuilder<TClient> ConfigureOptions(Action<SitecoreTokenServiceOptions> configureOptions)`
- `ISitecoreAuthenticationBuilder<TClient> ConfigureOptions(IConfiguration configuration, string sectionName = "SitecoreAuthentication")`
- `ISitecoreAuthenticationBuilder<TClient> ConfigureResilience(Action<HttpStandardResilienceOptions>? configureResilience = null)`
- `ISitecoreAuthenticationBuilder<TClient> ConfigureHttpClient(Action<IHttpClientBuilder> configureClient)`
- `ISitecoreAuthenticationBuilder<TClient> UseHttpClientFactory(Func<IServiceProvider, HttpClient> httpClientFactory)`

Private helpers: `ConfigureOptionsDelegate`, `ConfigureSingletonConnectionLifetime`, `AddCoreServices`, `AddTypedCoreServices<TClient>`, `GetOrAddTypedBuilder<TClient>`, and `TypedHttpClientFactoryHolder`
`[ServiceCollectionExtensions.cs:AddCoreServices]`, `[ServiceCollectionExtensions.cs:AddTypedCoreServices]`, `[ServiceCollectionExtensions.cs:GetOrAddTypedBuilder]`, `[ISitecoreAuthenticationBuilder.cs:TypedHttpClientFactoryHolder]`.

## Invariants

- `ISitecoreTokenCache` → `SitecoreTokenCache` is always registered as
  **Singleton** via `TryAddSingleton`, regardless of the service lifetime or
  whether untyped or typed `<TClient>` registration is used
  `[ServiceCollectionExtensions.cs:AddCoreServices]`,
  `[ServiceCollectionExtensions.cs:AddTypedCoreServices]`.
- `AddTypedCoreServices<TClient>` calls `services.AddOptions<SitecoreTokenServiceOptions>()`
  so default unnamed `IOptions<SitecoreTokenServiceOptions>` always resolves for
  `SitecoreTokenCache` even when only typed `<TClient>` registrations are used.
- `ISitecoreTokenService` and `ISitecoreTokenService<TClient>` are registered
  with `TryAdd` using `Scoped` (`AddSitecoreAuthentication*`) or `Singleton`
  (`AddSitecoreAuthenticationSingleton*`). A pre-registered user implementation
  of either interface is not replaced.
- `AddSitecoreAuthentication*` overloads taking `IConfiguration` validate
  `configuration` and `sectionName` upfront before mutating `IServiceCollection`.
- Typed `<TClient>` registrations never register `TClient` or
  `SitecoreAuthenticationBuilder<TClient>` in `IServiceCollection` and use
  prefixed internal names (`$"{Constants.SitecoreHttpClientName}.{typeName}"` and
  `$"{Constants.SitecoreOptionsNamePrefix}.{typeName}"` where `typeName` is
  `typeof(TClient).AssemblyQualifiedName ?? typeof(TClient).FullName ?? typeof(TClient).Name`)
  so they never collide with `services.AddHttpClient<TClient>()` or identically
  named types across assemblies.
- Repeated typed calls for the same `(IServiceCollection, TClient)` reuse the
  same `SitecoreAuthenticationBuilder<TClient>` and `TypedHttpClientFactoryHolder`
  via `ConditionalWeakTable<IServiceCollection, ConcurrentDictionary<Type, object>>`,
  keeping `.UseHttpClientFactory(...)` and `.ConfigureResilience(...)` idempotent
  without retaining `IServiceCollection` instances globally.
- Initial `Singleton` registrations configure `SocketsHttpHandler.PooledConnectionLifetime`
  to 2 minutes (`ConfigureSingletonConnectionLifetime`) while allowing callers
  to replace or customize it via `configureClient` or `ConfigureHttpClient`.
- `SitecoreAuthenticationBuilder<TClient>.ConfigureResilience` caches the
  `IHttpStandardResiliencePipelineBuilder` instance so multiple
  `.ConfigureResilience(...)` calls reconfigure the same handler rather than
  stacking duplicate resilience pipelines; passing `null` attaches
  `AddStandardResilienceHandler()` with default options.
- Default lifetimes are part of the public contract: `Scoped` for
  `AddSitecoreAuthentication`, `Singleton` for
  `AddSitecoreAuthenticationSingleton`.
- On the untyped path, `HttpClient` comes from `httpClientFactory(sp)` when supplied,
  otherwise from `IHttpClientFactory.CreateClient(Constants.SitecoreHttpClientName)`
  `[ServiceCollectionExtensions.cs:AddCoreServices]`. On the typed path, `HttpClient`
  comes from `builder.ClientFactoryHolder.Factory(sp)` (when configured via
  `.UseHttpClientFactory(...)`), otherwise from
  `IHttpClientFactory.CreateClient($"{Constants.SitecoreHttpClientName}.{typeName}")`
  `[ServiceCollectionExtensions.cs:AddTypedCoreServices]`. On both paths, a custom
  `HttpClient` factory bypasses the named client, so `configureResilience` and
  `configureClient` do not apply to the client it returns.
- On the untyped `AddCoreServices` path, `AddStandardResilienceHandler()` is attached
  to the named client only when `configureResilience` is non-null; otherwise no
  resilience handler is added `[ServiceCollectionExtensions.cs:AddCoreServices]`.
- When `configureOptions` is null, an empty `Configure<SitecoreTokenServiceOptions>(_ => { })`
  is registered so `IOptions<SitecoreTokenServiceOptions>` resolves with class
  defaults `[ServiceCollectionExtensions.cs:ConfigureOptionsDelegate]`.
- The `IConfiguration` overloads bind `configuration.GetSection(sectionName)`;
  the default section name is `"SitecoreAuthentication"`
  `[Constants.cs:DefaultConfigurationSectionName]`.
- The single-delegate and `(IConfiguration, string)` overloads exist to keep
  existing call sites unambiguous (no CS0121) alongside the optional-parameter
  overloads. Do not remove them.
- On the untyped path, `ILogger<SitecoreTokenService>` is resolved with `GetService`
  (optional) and `IHttpClientFactory`, `ISitecoreTokenCache`, and
  `IOptions<SitecoreTokenServiceOptions>` are resolved with `GetRequiredService`
  `[ServiceCollectionExtensions.cs:AddCoreServices]`. On the typed path,
  `ILogger<SitecoreTokenService<TClient>>` is resolved with `GetService` (optional)
  and `IOptionsMonitor<SitecoreTokenServiceOptions>` is resolved with
  `GetRequiredService` (wrapped via `Options.Create(...Get(optionsName))`)
  `[ServiceCollectionExtensions.cs:AddTypedCoreServices]`.

## Side Effects

- Mutates the supplied `IServiceCollection`: adds options configuration, a
  named `HttpClient` registration (`services.AddHttpClient`, called on every
  invocation), and the cache and service descriptors
  `[ServiceCollectionExtensions.cs:AddCoreServices]`,
  `[ServiceCollectionExtensions.cs:AddTypedCoreServices]`, and tracks typed
  builders per `IServiceCollection` via `ConditionalWeakTable`
  `[ServiceCollectionExtensions.cs:GetOrAddTypedBuilder]`.
- No network, disk, or other I/O at registration time.

## Verification

- Run: `dotnet test Sitecore.API.Foundation.Authorization.slnx --filter "FullyQualifiedName~ServiceCollectionExtensions|FullyQualifiedName~DependencyInjectionIntegrationTests|FullyQualifiedName~ResilienceTests"` (from the repository root)
