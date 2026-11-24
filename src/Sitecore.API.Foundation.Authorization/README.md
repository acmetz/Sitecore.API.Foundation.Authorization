# Sitecore.API.Foundation.Authorization

## Overview

The single library project, packaged to NuGet as `SitecoreAPIAuthorization`
([`Sitecore.API.Foundation.Authorization.csproj`](./Sitecore.API.Foundation.Authorization.csproj)). It targets `net10.0`
and `net11.0`, with `<LangVersion>15.0</LangVersion>`, `Nullable` enabled, and `ImplicitUsings`
disabled (`Directory.Build.props`). Package dependencies:
`Microsoft.Extensions.Configuration.Abstractions`,
`Microsoft.Extensions.DependencyInjection.Abstractions`,
`Microsoft.Extensions.Http`, `Microsoft.Extensions.Http.Resilience`,
`Microsoft.Extensions.Logging.Abstractions`,
`Microsoft.Extensions.Options`, and
`Microsoft.Extensions.Options.ConfigurationExtensions`
(versions pinned centrally in [`Directory.Packages.props`](../../Directory.Packages.props)). Internals are
visible to `Sitecore.API.Foundation.Tests` and
`Sitecore.API.Foundation.Authorization.IntegrationTests`.

## Subdirectories & Boundaries

- Abstractions: [./Abstractions/README.md](./Abstractions/README.md) - Public `ISitecoreTokenService`, `ISitecoreTokenService<TClient>`, and `ISitecoreTokenCache` contracts.
- Configuration: [./Configuration/README.md](./Configuration/README.md) - `SitecoreTokenServiceOptions` (cache limits, cleanup cadence, `AuthTokenUrl`) and internal OAuth2 `Constants` (`SitecoreHttpClientName`, `SitecoreOptionsNamePrefix`).
- DependencyInjection: [./DependencyInjection/README.md](./DependencyInjection/README.md) - Untyped and typed (`<TClient>`) `AddSitecoreAuthentication*` (Scoped) and `AddSitecoreAuthenticationSingleton*` registration, `ISitecoreAuthenticationBuilder<TClient>`, named `HttpClient`, optional standard resilience.
- Exceptions: [./Exceptions/README.md](./Exceptions/README.md) - `SitecoreAuthException` base with `SitecoreAuthHttpException` and `SitecoreAuthResponseException`.
- Models: [./Models/README.md](./Models/README.md) - Immutable `SitecoreAuthClientCredentials` and `SitecoreAuthToken` record structs with masked `ToString()`.
- Services: [./Services/README.md](./Services/README.md) - `SitecoreTokenService` and `SitecoreTokenService<TClient>` (OAuth2 token acquisition and refresh) and `SitecoreTokenCache` (thread-safe endpoint-partitioned in-memory cache with cleanup and eviction).

```mermaid
flowchart TD
    DI["DependencyInjection"] --> Services
    DI --> Abstractions
    DI --> Configuration
    Services --> Abstractions
    Services --> Configuration
    Services --> Exceptions
    Services --> Models
    Abstractions --> Models
```

`Models`, `Exceptions`, and `Configuration` reference no other folder in this
project, except that `SitecoreTokenServiceOptions.AuthTokenUrl` defaults to
`Constants.SitecoreCloudAuthTokenUrl` within `Configuration`.
