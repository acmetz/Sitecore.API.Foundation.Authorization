# Abstractions

## Overview

Public contracts for the library. Consumers depend on these interfaces; the
default implementations live in [`../Services/README.md`](../Services/README.md). Both interfaces use the value
types `SitecoreAuthClientCredentials` and `SitecoreAuthToken` from
[`../Models/README.md`](../Models/README.md).

## Interface

- `ISitecoreTokenService` — `[ISitecoreTokenService.cs:ISitecoreTokenService]`
  - `Task<SitecoreAuthToken> GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials)`
  - `Task<SitecoreAuthToken> GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials, CancellationToken cancellationToken)`
  - `Task<SitecoreAuthToken> TryRefreshSitecoreAuthToken(SitecoreAuthToken token)`
  - `Task<SitecoreAuthToken> TryRefreshSitecoreAuthToken(SitecoreAuthToken token, CancellationToken cancellationToken)`
- `ISitecoreTokenCache : IDisposable` — `[ISitecoreTokenCache.cs:ISitecoreTokenCache]`
  - `int CacheSize { get; }`
  - `SitecoreAuthToken? GetToken(SitecoreAuthClientCredentials credentials)` — returns `null` when missing or expired
  - `void SetToken(SitecoreAuthClientCredentials credentials, SitecoreAuthToken token)`
  - `SitecoreAuthClientCredentials? RemoveToken(SitecoreAuthToken token)` — returns the credentials that owned the token, or `null`
  - `void ClearCache()`
  - `void PerformCleanup()` — removes expired tokens (the interface XML doc also mentions enforcing size limits, which `SitecoreTokenCache` enforces on `SetToken`)

## Invariants

- Each async method on `ISitecoreTokenService` has two overloads: one without
  and one with a `CancellationToken`. Do not remove the single-parameter
  overloads; they are part of the public, backward-compatible surface
  `[ISitecoreTokenService.cs:GetSitecoreAuthToken]`,
  `[ISitecoreTokenService.cs:TryRefreshSitecoreAuthToken]`.
- `ISitecoreTokenCache` extends `IDisposable`; every implementation must be
  disposable `[ISitecoreTokenCache.cs:ISitecoreTokenCache]`.
- `ISitecoreTokenCache` is synchronous; no member returns a `Task`
  `[ISitecoreTokenCache.cs:ISitecoreTokenCache]`.
- `RemoveToken` looks up by token, not by credentials, and returns the owning
  credentials so callers can re-authenticate
  `[ISitecoreTokenCache.cs:RemoveToken]`.

## Side Effects

- None. This directory contains only interface declarations.

## Verification

- Run: `dotnet test Sitecore.API.Foundation.Authorization.slnx` (from the repository root)
