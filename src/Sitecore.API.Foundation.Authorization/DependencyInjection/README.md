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

Optional parameter types: `Action<HttpStandardResilienceOptions>? configureResilience`,
`Action<IHttpClientBuilder>? configureClient`,
`Func<IServiceProvider, HttpClient>? httpClientFactory`.

Private helpers: `ConfigureOptionsDelegate`, `AddCoreServices`
`[ServiceCollectionExtensions.cs:AddCoreServices]`.

## Invariants

- `ISitecoreTokenCache` → `SitecoreTokenCache` is always registered as
  **Singleton** via `TryAddSingleton`, regardless of the service lifetime
  `[ServiceCollectionExtensions.cs:AddCoreServices]`.
- `ISitecoreTokenService` is registered with `TryAdd` using `Scoped`
  (`AddSitecoreAuthentication*`) or `Singleton`
  (`AddSitecoreAuthenticationSingleton*`). A pre-registered user
  implementation of either interface is not replaced
  `[ServiceCollectionExtensions.cs:AddCoreServices]`.
- Default lifetimes are part of the public contract: `Scoped` for
  `AddSitecoreAuthentication`, `Singleton` for
  `AddSitecoreAuthenticationSingleton`.
- The service's `HttpClient` comes from `httpClientFactory(sp)` when supplied;
  otherwise from `IHttpClientFactory.CreateClient(Constants.SitecoreHttpClientName)`
  `[ServiceCollectionExtensions.cs:AddCoreServices]`. A custom
  `httpClientFactory` bypasses the named client, so `configureResilience` and
  `configureClient` do not apply to the client it returns.
- `AddStandardResilienceHandler()` is attached to the named client only when
  `configureResilience` is non-null; otherwise no resilience handler is added
  `[ServiceCollectionExtensions.cs:AddCoreServices]`.
- When `configureOptions` is null, an empty `Configure<SitecoreTokenServiceOptions>(_ => { })`
  is registered so `IOptions<SitecoreTokenServiceOptions>` resolves with class
  defaults `[ServiceCollectionExtensions.cs:ConfigureOptionsDelegate]`.
- The `IConfiguration` overloads bind `configuration.GetSection(sectionName)`;
  the default section name is `"SitecoreAuthentication"`
  `[ServiceCollectionExtensions.cs:DefaultConfigurationSectionName]`.
- The single-delegate and `(IConfiguration, string)` overloads exist to keep
  existing call sites unambiguous (no CS0121) alongside the optional-parameter
  overloads. Do not remove them.
- `ILogger<SitecoreTokenService>` is resolved with `GetService` (optional);
  `IHttpClientFactory`, `ISitecoreTokenCache`, and
  `IOptions<SitecoreTokenServiceOptions>` are resolved with
  `GetRequiredService` `[ServiceCollectionExtensions.cs:AddCoreServices]`.

## Side Effects

- Mutates the supplied `IServiceCollection`: adds options configuration, a
  named `HttpClient` registration (`services.AddHttpClient`, called on every
  invocation), and the cache and service descriptors
  `[ServiceCollectionExtensions.cs:AddCoreServices]`.
- No network, disk, or other I/O at registration time.

## Verification

- Run: `dotnet test Sitecore.API.Foundation.Authorization.slnx --filter "FullyQualifiedName~ServiceCollectionExtensions|FullyQualifiedName~DependencyInjectionIntegrationTests|FullyQualifiedName~ResilienceTests"` (from the repository root)
