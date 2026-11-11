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
  | `AuthTokenUrl` | `string` | `Constants.SitecoreCloudAuthTokenUrl` | `SitecoreTokenService` POST target |

- `Constants` (`internal static class`) — `[Constants.cs:Constants]`
  - `SitecoreAuthAudience = "https://api.sitecorecloud.io"`
  - `SitecoreAuthGrantType = "client_credentials"`
  - `SitecoreCloudAuthTokenUrl = "https://auth.sitecorecloud.io/oauth/token"`
  - `SitecoreHttpClientName = "Sitecore.API.Foundation.Authorization.HttpClient"`

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
- `SitecoreHttpClientName` is the single named `HttpClient` key shared by
  registration and resolution `[Constants.cs:SitecoreHttpClientName]`.

## Side Effects

- None. This directory contains only data and constants.

## Verification

- Run: `dotnet test Sitecore.API.Foundation.Authorization.slnx` (from the repository root)
