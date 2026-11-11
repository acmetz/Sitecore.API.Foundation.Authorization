# Models

## Overview

Immutable value types exchanged across the public API: the OAuth2 client
credentials used as the cache key, and the bearer token returned to callers.
Both mask their secret field in `ToString()` so they are safe to pass to
loggers.

## Interface

- `SitecoreAuthClientCredentials` — `public readonly record struct SitecoreAuthClientCredentials(string ClientId, string ClientSecret)` `[SitecoreAuthClientCredentials.cs:SitecoreAuthClientCredentials]`
  - `string ClientId { get; }`
  - `string ClientSecret { get; }`
  - `override string ToString()` → `SitecoreAuthClientCredentials { ClientId = <id>, ClientSecret = *** }`
- `SitecoreAuthToken` — `public readonly record struct SitecoreAuthToken(string AccessToken, DateTimeOffset Expiration)` `[SitecoreAuthToken.cs:SitecoreAuthToken]`
  - `string AccessToken { get; }`
  - `DateTimeOffset Expiration { get; }`
  - `bool IsExpired` → `DateTimeOffset.UtcNow >= Expiration`
  - `override string ToString()` → `SitecoreAuthToken { AccessToken = ***, Expiration = <expiration> }`

## Invariants

- Constructing `SitecoreAuthClientCredentials` with a null or empty `ClientId`
  or `ClientSecret` throws `ArgumentNullException`
  `[SitecoreAuthClientCredentials.cs:ClientId]`,
  `[SitecoreAuthClientCredentials.cs:ClientSecret]`.
- Constructing `SitecoreAuthToken` with a null or empty `AccessToken` throws
  `ArgumentNullException` `[SitecoreAuthToken.cs:AccessToken]`. `Expiration`
  is not validated.
- `ToString()` must never emit `ClientSecret` or `AccessToken`; both are
  replaced with `***` `[SitecoreAuthClientCredentials.cs:ToString]`,
  `[SitecoreAuthToken.cs:ToString]`.
- Both types use compiler-generated record value equality over all fields.
  `SitecoreTokenCache` relies on this: credentials (`ClientId` +
  `ClientSecret`) are the dictionary key, and `RemoveToken` matches tokens by
  `AccessToken` + `Expiration`.
- `IsExpired` compares against `DateTimeOffset.UtcNow` at each read; it is not
  cached `[SitecoreAuthToken.cs:IsExpired]`.
- `default(SitecoreAuthClientCredentials)` and `default(SitecoreAuthToken)`
  bypass the constructor checks (struct semantics) and carry null strings.

## Side Effects

- None. Reading `IsExpired` reads the system clock.

## Verification

- Run: `dotnet test Sitecore.API.Foundation.Authorization.slnx --filter "FullyQualifiedName~SitecoreAuthTokenTests|FullyQualifiedName~SitecoreAuthClientCredentialsTests"` (from the repository root)
