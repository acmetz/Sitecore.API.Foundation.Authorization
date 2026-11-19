# Configuration

## Overview

Runtime options for the token service and cache, plus internal constants for
the Sitecore Cloud OAuth2 `client_credentials` request. Options are bound
through `IOptions<SitecoreTokenServiceOptions>` by the registration methods in
[`../DependencyInjection/README.md`](../DependencyInjection/README.md).

## Interface

- `SitecoreTokenServiceOptions` (public class) — `[SitecoreTokenServiceOptions.cs:SitecoreTokenServiceOptions]`

  | Property | Type | Default | Consumer |
  |---|---|---|---|
  | `MaxCacheSize` | `int` | `10` | `SitecoreTokenCache.SetToken` eviction trigger |
  | `CleanupThreshold` | `int` | `15` | `SitecoreTokenCache` opportunistic cleanup trigger |
  | `CleanupInterval` | `TimeSpan` | `TimeSpan.FromMinutes(5)` | `SitecoreTokenCache` opportunistic cleanup trigger |
  | `AuthTokenUrl` | `string` | `Constants.SitecoreCloudAuthTokenUrl` | `SitecoreTokenService` POST target and `SitecoreTokenCache` default partition URL |

- `Constants` (`internal static class`) — `[Constants.cs:Constants]`
  - `SitecoreAuthAudience = "https://api.sitecorecloud.io"`
  - `SitecoreAuthGrantType = "client_credentials"`
  - `SitecoreCloudAuthTokenUrl = "https://auth.sitecorecloud.io/oauth/token"`
  - `SitecoreHttpClientName = "Sitecore.API.Foundation.Authorization.HttpClient"`
  - `SitecoreOptionsNamePrefix = "Sitecore.API.Foundation.Authorization.Options"`
  - `DefaultConfigurationSectionName = "SitecoreAuthentication"`

## Invariants

- `Constants` is `internal`. Only this assembly and the assemblies listed in
  `InternalsVisibleTo` in [`../Sitecore.API.Foundation.Authorization.csproj`](../Sitecore.API.Foundation.Authorization.csproj)
  (`Sitecore.API.Foundation.Tests`,
  `Sitecore.API.Foundation.Authorization.IntegrationTests`) can read it
  `[Constants.cs:Constants]`.
- `AuthTokenUrl` defaults to the production endpoint
  `[SitecoreTokenServiceOptions.cs:AuthTokenUrl]`. The options class does not
  validate it; `SitecoreTokenService.GetSitecoreAuthToken` rejects values that
  are not absolute HTTPS (or loopback HTTP) URIs or that contain `UserInfo`.
- The options class performs no validation of `MaxCacheSize`,
  `CleanupThreshold`, or `CleanupInterval`; it is a plain settable POCO
  `[SitecoreTokenServiceOptions.cs:SitecoreTokenServiceOptions]`.
- `MaxCacheSize`, `CleanupThreshold`, and `CleanupInterval` are read by the
  shared singleton `SitecoreTokenCache` from the default unnamed options
  registration (`Options.DefaultName`), while typed `<TClient>` registrations
  read per-type named options (`$"{Constants.SitecoreOptionsNamePrefix}.{typeName}"`)
  for `AuthTokenUrl` `[Constants.cs:SitecoreOptionsNamePrefix]`.
- `Constants.DefaultConfigurationSectionName` (`"SitecoreAuthentication"`) is the
  shared default section name used across `IConfiguration` DI overloads and
  `ISitecoreAuthenticationBuilder<TClient>.ConfigureOptions`
  `[Constants.cs:DefaultConfigurationSectionName]`.
- `SitecoreHttpClientName` is the base named `HttpClient` key for untyped
  registration and resolution, while typed `<TClient>` registrations append
  `.{typeName}` (`$"{Constants.SitecoreHttpClientName}.{typeName}"`) for
  per-type `HttpClient` isolation `[Constants.cs:SitecoreHttpClientName]`.

## Side Effects

- None. This directory contains only data and constants.

## Verification

- Run: `dotnet test Sitecore.API.Foundation.Authorization.slnx` (from the repository root)
