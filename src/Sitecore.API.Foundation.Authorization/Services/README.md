# Services

## Overview

Default implementations of the `ISitecoreTokenService` and
`ISitecoreTokenCache` contracts from `../Abstractions/`.
`SitecoreTokenService` returns a cached token when one is valid; otherwise it
POSTs an OAuth2 `client_credentials` request to the configured `AuthTokenUrl`,
validates the response, and caches the result. `SitecoreTokenCache` is an
in-memory, thread-safe store keyed by `SitecoreAuthClientCredentials`, with
opportunistic expired-token cleanup and size-bounded eviction.

## Interface

- `public class SitecoreTokenService : ISitecoreTokenService` — `[SitecoreTokenService.cs:SitecoreTokenService]`
  - `SitecoreTokenService(HttpClient httpClient, ISitecoreTokenCache tokenCache, IOptions<SitecoreTokenServiceOptions> options, ILogger<SitecoreTokenService>? logger = null)`
  - `protected readonly HttpClient HttpClient`
  - `Task<SitecoreAuthToken> GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials)` → delegates with `CancellationToken.None`
  - `Task<SitecoreAuthToken> GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials, CancellationToken cancellationToken)`
  - `Task<SitecoreAuthToken> TryRefreshSitecoreAuthToken(SitecoreAuthToken token)` → delegates with `CancellationToken.None`
  - `Task<SitecoreAuthToken> TryRefreshSitecoreAuthToken(SitecoreAuthToken token, CancellationToken cancellationToken)`
  - Private nested `AuthResponse` DTO: `access_token` → `AccessToken`, `expires_in` → `ExpiresIn` `[SitecoreTokenService.cs:AuthResponse]`
- `public class SitecoreTokenCache : ISitecoreTokenCache` — `[SitecoreTokenCache.cs:SitecoreTokenCache]`
  - `SitecoreTokenCache(IOptions<SitecoreTokenServiceOptions> options, ILogger<SitecoreTokenCache>? logger = null)`
  - `int CacheSize`, `GetToken`, `SetToken`, `RemoveToken`, `ClearCache`, `PerformCleanup`, `Dispose()`
  - `protected virtual void Dispose(bool disposing)`

## Invariants

### `SitecoreTokenService`

- Constructor throws `ArgumentNullException` for a null `httpClient`,
  `tokenCache`, or `options`/`options.Value`; `logger` is optional
  `[SitecoreTokenService.cs:SitecoreTokenService]`.
- Constructor signature and `protected readonly HttpClient HttpClient` are part
  of the backward-compatible public surface; subclasses may read `HttpClient`.
- If `cancellationToken` is already cancelled on entry, both async methods
  throw `TaskCanceledException` before any cache or network access
  `[SitecoreTokenService.cs:GetSitecoreAuthToken]`,
  `[SitecoreTokenService.cs:TryRefreshSitecoreAuthToken]`.
- A valid cached token is returned without validating `AuthTokenUrl` or making
  an HTTP call `[SitecoreTokenService.cs:GetSitecoreAuthToken]`.
- On a cache miss, `AuthTokenUrl` must parse as an absolute URI with no
  `UserInfo`, and use `https`, or `http` only when the host is loopback;
  otherwise `SitecoreAuthException` is thrown
  `[SitecoreTokenService.cs:GetSitecoreAuthToken]`.
- The request body is JSON with `audience = Constants.SitecoreAuthAudience`,
  `grant_type = Constants.SitecoreAuthGrantType`, `client_id`, and
  `client_secret` `[SitecoreTokenService.cs:GetSitecoreAuthToken]`.
- Response handling, in order `[SitecoreTokenService.cs:GetSitecoreAuthToken]`:
  1. Non-success status → `SitecoreAuthHttpException(statusCode, AuthTokenUrl, ...)`.
  2. JSON read failure (any exception except `OperationCanceledException`) →
     `SitecoreAuthResponseException` wrapping the cause.
  3. Null result or null/empty `access_token` → `SitecoreAuthResponseException`.
  4. `expires_in <= 0` → `SitecoreAuthResponseException`.
- `Expiration` is computed as `DateTimeOffset.UtcNow.AddSeconds(expires_in)`
  after the response is read; no clock-skew margin is subtracted
  `[SitecoreTokenService.cs:GetSitecoreAuthToken]`.
- Logs never include `ClientSecret`, the access token, or the raw response
  body; they include `ClientId`, `AuthTokenUrl`, status code, `expires_in`, and
  expiration `[SitecoreTokenService.cs:GetSitecoreAuthToken]`.
- No lock is held between the cache lookup and `SetToken`; concurrent misses
  for the same credentials each issue their own HTTP request, and the last
  `SetToken` wins `[SitecoreTokenService.cs:GetSitecoreAuthToken]`.
- `TryRefreshSitecoreAuthToken` removes the token from the cache first, then
  calls `GetSitecoreAuthToken` with the owning credentials. If the token is
  not in the cache, it throws `ArgumentException` (`paramName = "token"`)
  `[SitecoreTokenService.cs:TryRefreshSitecoreAuthToken]`.
- All awaits use `ConfigureAwait(false)`.

### `SitecoreTokenCache`

- Storage is `ConcurrentDictionary<SitecoreAuthClientCredentials, SitecoreAuthToken>`;
  one token per credentials pair `[SitecoreTokenCache.cs:_tokens]`.
- `GetToken` returns `null` for a missing or expired entry and does not remove
  the expired entry itself `[SitecoreTokenCache.cs:TryGetValidToken]`.
- Cleanup runs only on a `GetToken` miss (no background timer), when any of
  these is true `[SitecoreTokenCache.cs:ShouldPerformCleanup]`:
  - `Count > CleanupThreshold`
  - `UtcNow - LastCleanup > CleanupInterval`
  - one of the first `min(5, Count)` enumerated entries is expired
    `[SitecoreTokenCache.cs:HasExpiredTokens]`
- Opportunistic cleanup waits at most 10 ms for the write lock and is skipped
  if it cannot acquire it `[SitecoreTokenCache.cs:TryPerformCleanup]`.
  `PerformCleanup()` blocks until it acquires the lock
  `[SitecoreTokenCache.cs:PerformCleanup]`.
- Removal uses `TryRemove(KeyValuePair)`, so a token replaced concurrently by
  `SetToken` is never deleted by cleanup, eviction, or `RemoveToken`; removal
  counts reflect only successful removals
  `[SitecoreTokenCache.cs:CleanupExpiredTokensUnsafe]`,
  `[SitecoreTokenCache.cs:EvictOldestTokensUnsafe]`,
  `[SitecoreTokenCache.cs:RemoveToken]`.
- When `SetToken` leaves `Count > MaxCacheSize`, eviction removes
  `Count - MaxCacheSize` entries with the earliest `Expiration`, selected with
  a bounded max-heap `PriorityQueue` `[SitecoreTokenCache.cs:EvictOldestTokensUnsafe]`.
  Eviction waits at most 50 ms for the write lock and is skipped if it cannot
  acquire it, so the cache can temporarily exceed `MaxCacheSize`
  `[SitecoreTokenCache.cs:TryEvictOldestTokens]`.
- `RemoveToken` matches by record value equality of the token and returns the
  owning credentials, or `null` `[SitecoreTokenCache.cs:RemoveToken]`.
- `ClearCache` does not take the cleanup lock; it clears the dictionary and
  resets `LastCleanup` to `DateTimeOffset.MinValue`
  `[SitecoreTokenCache.cs:ClearCache]`.
- `LastCleanup` is stored as ticks and read and written with `Interlocked`
  `[SitecoreTokenCache.cs:LastCleanup]`.
- Logs include `ClientId`, expiration, and counts; never the secret or the
  access token.

## Side Effects

- `SitecoreTokenService.GetSitecoreAuthToken` on a cache miss: one outbound
  HTTP `POST` to `SitecoreTokenServiceOptions.AuthTokenUrl` via
  `HttpClient.PostAsJsonAsync`, then `ISitecoreTokenCache.SetToken`
  `[SitecoreTokenService.cs:GetSitecoreAuthToken]`.
- `SitecoreTokenService.TryRefreshSitecoreAuthToken`: removes the old token
  from the cache, then performs the above
  `[SitecoreTokenService.cs:TryRefreshSitecoreAuthToken]`.
- `SitecoreTokenCache`: in-process memory mutations only; holds a
  `ReaderWriterLockSlim` (`_cleanupLock`) released by `Dispose()`
  `[SitecoreTokenCache.cs:Dispose]`.
- Both classes write `ILogger` entries (Debug/Information/Warning/Error) when
  a logger is supplied.

## Verification

- Run: `dotnet test Sitecore.API.Foundation.Authorization.slnx --filter "FullyQualifiedName~SitecoreTokenServiceTests|FullyQualifiedName~SitecoreTokenServiceLoggingTests|FullyQualifiedName~SitecoreTokenCacheTests|FullyQualifiedName~SitecoreTokenCacheLoggingTests"` (from the repository root)
