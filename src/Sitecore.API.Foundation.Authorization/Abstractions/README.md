# Abstractions

## Overview

Public contracts for the library. Consumers depend on these interfaces; the
default implementations live in [`../Services/README.md`](../Services/README.md). All three interfaces use the value
types `SitecoreAuthClientCredentials` and `SitecoreAuthToken` from
[`../Models/README.md`](../Models/README.md).

## Interface

- `ISitecoreTokenService` — `[ISitecoreTokenService.cs:ISitecoreTokenService]`
  - `Task<SitecoreAuthToken> GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials)`
  - `Task<SitecoreAuthToken> GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials, CancellationToken cancellationToken)`
  - `Task<SitecoreAuthToken> TryRefreshSitecoreAuthToken(SitecoreAuthToken token)`
  - `Task<SitecoreAuthToken> TryRefreshSitecoreAuthToken(SitecoreAuthToken token, CancellationToken cancellationToken)`
- `ISitecoreTokenService<TClient> : ISitecoreTokenService where TClient : class` — `[ISitecoreTokenService.cs:ISitecoreTokenService<TClient>]`
  - Marker generic contract inheriting all `ISitecoreTokenService` operations for consumer type `TClient`.
- `ISitecoreTokenCache : IDisposable` — `[ISitecoreTokenCache.cs:ISitecoreTokenCache]`
  - `int CacheSize { get; }`
  - `SitecoreAuthToken? GetToken(SitecoreAuthClientCredentials credentials)` — returns `null` when missing or expired
  - `SitecoreAuthToken? GetToken(SitecoreAuthClientCredentials credentials, string authTokenUrl) => GetToken(credentials)` — endpoint-partitioned lookup with C# default interface forwarding
  - `void SetToken(SitecoreAuthClientCredentials credentials, SitecoreAuthToken token)`
  - `void SetToken(SitecoreAuthClientCredentials credentials, SitecoreAuthToken token, string authTokenUrl) => SetToken(credentials, token)` — endpoint-partitioned store with C# default interface forwarding
  - `SitecoreAuthClientCredentials? RemoveToken(SitecoreAuthToken token)` — returns the credentials that owned the token, or `null`
  - `SitecoreAuthClientCredentials? RemoveToken(SitecoreAuthToken token, string authTokenUrl) => RemoveToken(token)` — endpoint-partitioned removal with C# default interface forwarding
  - `void ClearCache()`
  - `void PerformCleanup()` — removes expired tokens (the interface XML doc also mentions enforcing size limits, which `SitecoreTokenCache` enforces on `SetToken`)

## Invariants

- Each async method on `ISitecoreTokenService` has two overloads: one without
  and one with a `CancellationToken`. Do not remove the single-parameter
  overloads; they are part of the public, backward-compatible surface
  `[ISitecoreTokenService.cs:GetSitecoreAuthToken]`,
  `[ISitecoreTokenService.cs:TryRefreshSitecoreAuthToken]`.
- `ISitecoreTokenService<TClient>` resides in `Abstractions` and depends only on
  `Models`, preserving the acyclic module dependency graph.
- `ISitecoreTokenCache` provides C# default interface implementations on the
  `authTokenUrl`-aware overloads forwarding to the 1-arg/2-arg methods so
  existing custom `ISitecoreTokenCache` implementations remain source and binary
  compatible `[ISitecoreTokenCache.cs:ISitecoreTokenCache]`.
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
