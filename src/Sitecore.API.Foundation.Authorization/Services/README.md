# Services

## Overview

Default implementations of the `ISitecoreTokenService`,
`ISitecoreTokenService<TClient>`, and `ISitecoreTokenCache` contracts from
[`../Abstractions/README.md`](../Abstractions/README.md).
`SitecoreTokenService` (and its typed subclass `SitecoreTokenService<TClient>`)
returns a cached token for `(Credentials, AuthTokenUrl)` when one is valid;
otherwise it POSTs an OAuth2 `client_credentials` request to the configured
`AuthTokenUrl`, validates the response, and caches the result.
`SitecoreTokenCache` is an in-memory, thread-safe store keyed by
`SitecoreTokenCacheKey(SitecoreAuthClientCredentials Credentials, string AuthTokenUrl)`,
with opportunistic expired-token cleanup and size-bounded eviction.

## Interface

- `public class SitecoreTokenService : ISitecoreTokenService` — `[SitecoreTokenService.cs:SitecoreTokenService]`
  - `SitecoreTokenService(HttpClient httpClient, ISitecoreTokenCache tokenCache, IOptions<SitecoreTokenServiceOptions> options, ILogger<SitecoreTokenService>? logger = null)`
  - `protected SitecoreTokenService(HttpClient httpClient, ISitecoreTokenCache tokenCache, IOptions<SitecoreTokenServiceOptions> options, ILogger? logger)`
  - `protected readonly HttpClient HttpClient`
  - `Task<SitecoreAuthToken> GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials)` → delegates with `CancellationToken.None`
  - `Task<SitecoreAuthToken> GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials, CancellationToken cancellationToken)`
  - `Task<SitecoreAuthToken> TryRefreshSitecoreAuthToken(SitecoreAuthToken token)` → delegates with `CancellationToken.None`
  - `Task<SitecoreAuthToken> TryRefreshSitecoreAuthToken(SitecoreAuthToken token, CancellationToken cancellationToken)`
  - Private nested `AuthRequest` and `AuthResponse` `readonly record struct` DTOs `[SitecoreTokenService.cs:AuthRequest]`, `[SitecoreTokenService.cs:AuthResponse]`
- `public class SitecoreTokenService<TClient> : SitecoreTokenService, ISitecoreTokenService<TClient> where TClient : class` — `[SitecoreTokenService.cs:SitecoreTokenService<TClient>]`
  - `SitecoreTokenService(HttpClient httpClient, ISitecoreTokenCache tokenCache, IOptions<SitecoreTokenServiceOptions> options, ILogger<SitecoreTokenService<TClient>>? logger = null)`
- `public class SitecoreTokenCache : ISitecoreTokenCache` — `[SitecoreTokenCache.cs:SitecoreTokenCache]`
  - `SitecoreTokenCache(IOptions<SitecoreTokenServiceOptions> options, ILogger<SitecoreTokenCache>? logger = null)`
  - `int CacheSize`, `GetToken(credentials)`, `GetToken(credentials, authTokenUrl)`, `SetToken(credentials, token)`, `SetToken(credentials, token, authTokenUrl)`, `RemoveToken(token)`, `RemoveToken(token, authTokenUrl)`, `ClearCache`, `PerformCleanup`, `Dispose()`
  - `protected virtual void Dispose(bool disposing)`

## Invariants

### `SitecoreTokenService`

- Constructor throws `ArgumentNullException` for a null `httpClient`,
  `tokenCache`, or `options`/`options.Value`; `logger` is optional
  `[SitecoreTokenService.cs:SitecoreTokenService]`.
- Constructor signature and `protected readonly HttpClient HttpClient` are part
  of the backward-compatible public surface; subclasses (including
  `SitecoreTokenService<TClient>`) may read `HttpClient` and pass a category-typed
  `ILogger` via the `protected` constructor.
- For any concrete `ISitecoreTokenCache` implementation (including
  `SitecoreTokenCache` and custom classes implementing `ISitecoreTokenCache`),
  `SitecoreTokenService` calls the `AuthTokenUrl`-aware cache overloads
  (`GetToken(credentials, _options.AuthTokenUrl)`,
  `SetToken(credentials, sitecoreToken, _options.AuthTokenUrl)`, and
  `RemoveToken(token, _options.AuthTokenUrl)`) directly via private helpers
  (`GetCachedToken`, `CacheToken`, `RemoveCachedToken`) without falling back to
  1-arg/2-arg overloads, guaranteeing strict endpoint isolation and single
  invocation per operation (with C# Default Interface Methods forwarding to
  1-arg/2-arg methods for legacy custom caches that do not override the
  `authTokenUrl` overloads). When `_tokenCache` is a runtime dynamic proxy mock
  (detected once in the constructor via `IsDynamicProxyCache(_tokenCache)` for
  `typeof(SitecoreTokenCache)` fast-path exclusion, `DispatchProxy`,
  `Castle.Proxies`, or `DynamicProxyGenAssembly2`), `GetCachedToken` and
  `RemoveCachedToken` try the `authTokenUrl` overload first and coalesce (`??`)
  to the 1-arg overload, while `CacheToken` writes both 3-arg and 2-arg
  `SetToken` so legacy proxy setups receive all expected calls.
- `GetSitecoreAuthToken` validates non-empty `ClientId` and `ClientSecret` (guarding
  against `default(SitecoreAuthClientCredentials)`) and validates `_options.AuthTokenUrl`
  via `ValidateAuthTokenUrl` (using a volatile reference-equality cache after first
  validation) **before** querying or modifying the cache, and `TryRefreshSitecoreAuthToken`
  likewise validates `_options.AuthTokenUrl` before removing any cached token.
- If `cancellationToken` is already cancelled on entry, both async methods
  throw `TaskCanceledException` before any cache or network access
  `[SitecoreTokenService.cs:GetSitecoreAuthToken]`,
  `[SitecoreTokenService.cs:TryRefreshSitecoreAuthToken]`.
- A valid cached token is returned after credential and `AuthTokenUrl` validation
  without making an HTTP call `[SitecoreTokenService.cs:GetSitecoreAuthToken]`.
- On validation, `AuthTokenUrl` must parse as an absolute URI with no
  `UserInfo`, and use `https`, or `http` only when the host is loopback;
  otherwise `SitecoreAuthException` is thrown
  `[SitecoreTokenService.cs:ValidateAuthTokenUrl]`.
- The request body is JSON with `audience = Constants.SitecoreAuthAudience`,
  `grant_type = Constants.SitecoreAuthGrantType`, `client_id`, and
  `client_secret` `[SitecoreTokenService.cs:GetSitecoreAuthToken]`.
- Response handling, in order `[SitecoreTokenService.cs:GetSitecoreAuthToken]`:
  1. Non-success status → `SitecoreAuthHttpException(statusCode, AuthTokenUrl, ...)`.
  2. JSON read failure (any exception except `OperationCanceledException`) →
     `SitecoreAuthResponseException` wrapping the cause.
  3. Null result or null/whitespace `access_token` → `SitecoreAuthResponseException`.
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

- Constructor throws `ArgumentNullException` when `options` or `options.Value`
  is `null`; `logger` is optional `[SitecoreTokenCache.cs:SitecoreTokenCache]`.
- Storage is `ConcurrentDictionary<SitecoreTokenCacheKey, SitecoreAuthToken>`,
  where `SitecoreTokenCacheKey(SitecoreAuthClientCredentials Credentials, string AuthTokenUrl)`
  normalizes null/whitespace `authTokenUrl` to `_options.AuthTokenUrl?.Trim() ?? string.Empty`
  and trims non-empty URLs `[SitecoreTokenCache.cs:_tokens]`, `[SitecoreTokenCache.cs:NormalizeAuthTokenUrl]`.
- The 1-arg `GetToken(credentials)` and 2-arg `SetToken(credentials, token)`
  delegate to the `authTokenUrl`-aware overloads using `_options.AuthTokenUrl`.
  The 2-arg `RemoveToken(token, authTokenUrl)` only removes an entry whose both
  token value and normalized `AuthTokenUrl` match, preventing cross-endpoint
  eviction during token refresh.
- `GetToken` returns `null` for a missing or expired entry and does not remove
  the expired entry itself `[SitecoreTokenCache.cs:TryGetValidToken]`.
- Cleanup runs only on a `GetToken` miss (no background timer), when any of
  these is true `[SitecoreTokenCache.cs:ShouldPerformCleanup]`:
  - `UtcNow - LastCleanup > CleanupInterval`
  - `Count > CleanupThreshold`
  - one of the first up to 5 enumerated entries is expired
    `[SitecoreTokenCache.cs:HasExpiredTokens]`
- Opportunistic cleanup (`TryPerformCleanup`) waits at most 10 ms for
  `_cleanupLock`, is skipped if it cannot acquire the lock, and re-evaluates
  `ShouldPerformCleanup()` under the lock (double-checked locking) before
  calling `ExecuteCleanupUnsafe()` `[SitecoreTokenCache.cs:TryPerformCleanup]`.
- `PerformCleanup()` blocks until it acquires `_cleanupLock` and calls
  `ExecuteCleanupUnsafe()` directly (which removes expired tokens and updates
  `LastCleanup = DateTimeOffset.UtcNow` even when `removed == 0`, but does not
  run size-limit eviction) `[SitecoreTokenCache.cs:PerformCleanup]`,
  `[SitecoreTokenCache.cs:ExecuteCleanupUnsafe]`.
- Removal uses `TryRemove(KeyValuePair)`, so a token replaced concurrently by
  `SetToken` is never deleted by cleanup, eviction, or `RemoveToken`; removal
  counts reflect only successful removals
  `[SitecoreTokenCache.cs:CleanupExpiredTokensUnsafe]`,
  `[SitecoreTokenCache.cs:EvictOldestTokensUnsafe]`,
  `[SitecoreTokenCache.cs:RemoveTokenCore]`.
- When `SetToken` inserts/updates via `_tokens[key] = token` and leaves `Count > MaxCacheSize`,
  eviction removes `Count - Math.Max(0, MaxCacheSize)` entries with the earliest
  `Expiration.UtcTicks`, selected with a bounded max-heap `PriorityQueue` using
  the static `DescendingLongComparer` (avoiding per-eviction comparer allocation)
  `[SitecoreTokenCache.cs:EvictOldestTokensUnsafe]`,
  `[SitecoreTokenCache.cs:DescendingLongComparer]`.
  Eviction waits at most 50 ms for `_cleanupLock` and is skipped if it cannot
  acquire it, so the cache can temporarily exceed `MaxCacheSize`
  `[SitecoreTokenCache.cs:TryEvictOldestTokens]`.
- `RemoveToken(token)` and `RemoveToken(token, authTokenUrl)` delegate to
  `RemoveTokenCore(token, normalizedAuthTokenUrl)`, matching by record value
  equality of the token (and normalized `AuthTokenUrl` when specified) and
  returning the owning credentials, or `null`
  `[SitecoreTokenCache.cs:RemoveToken]`,
  `[SitecoreTokenCache.cs:RemoveTokenCore]`.
- `ClearCache` acquires `_cleanupLock`, clears `_tokens`, and resets
  `LastCleanup` to `DateTimeOffset.MinValue` before logging the removed count
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
- `SitecoreTokenCache`: in-process memory mutations only; synchronizes cleanup
  and eviction with `System.Threading.Lock` (`_cleanupLock`) and preserves
  `Dispose()` as a no-op for `ISitecoreTokenCache : IDisposable` compatibility
  `[SitecoreTokenCache.cs:Dispose]`.
- Both classes write `ILogger` entries (Debug/Information/Warning/Error) when
  a logger is supplied.

## Verification

- Run: `dotnet test Sitecore.API.Foundation.Authorization.slnx --filter "FullyQualifiedName~SitecoreTokenServiceTests|FullyQualifiedName~SitecoreTokenServiceLoggingTests|FullyQualifiedName~SitecoreTokenCacheTests|FullyQualifiedName~SitecoreTokenCacheLoggingTests"` (from the repository root)
