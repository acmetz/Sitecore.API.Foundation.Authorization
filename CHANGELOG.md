# Changelog

All notable changes to this project will be documented in this file.

## [1.1.0] - 2026-11-19
### Summary
- Modernization to `.NET 10` (LTS) and `.NET 11` (STS) target frameworks and introduction of typed multi-client authentication registration (`AddSitecoreAuthentication<TClient>()` / `ISitecoreTokenService<TClient>`) with endpoint-partitioned shared token caching.

### Added
- Target framework support for `.NET 11.0` (`net11.0`) alongside `.NET 10.0` (`net10.0`) with `Microsoft.Extensions.*` `11.0.0`.
- Typed token service contract `ISitecoreTokenService<TClient> : ISitecoreTokenService` and implementation `SitecoreTokenService<TClient>`.
- Fluent registration builder `ISitecoreAuthenticationBuilder<TClient>` supporting per-client `.ConfigureOptions(...)`, `.ConfigureResilience(...)`, `.ConfigureHttpClient(...)`, and `.UseHttpClientFactory(...)`.
- Generic dependency injection registration methods `AddSitecoreAuthentication<TClient>()` (scoped) and `AddSitecoreAuthenticationSingleton<TClient>()` (singleton) designed to coexist cleanly with `services.AddHttpClient<TClient>()`.
- Endpoint-partitioned `ISitecoreTokenCache` overloads (`GetToken`, `SetToken`, `RemoveToken` accepting `authTokenUrl`) with default interface method forwarding for backward compatibility.

### Changed
- Upgraded `<LangVersion>` to `15.0` across the solution (`Directory.Build.props`) and added direct `Microsoft.Extensions.Options.ConfigurationExtensions` `11.0.0` reference.
- `SitecoreTokenCache` now partitions cached entries by composite key `(Credentials, AuthTokenUrl)` so a single shared singleton cache safely isolates tokens across multiple Sitecore authentication endpoints.
- Replaced `ReaderWriterLockSlim` with `.NET` `System.Threading.Lock` in `SitecoreTokenCache`, synchronized `ClearCache()` under the cleanup lock, hoisted a static `DescendingLongComparer` for `PriorityQueue` eviction, simplified updates via direct `_tokens[key] = token` indexing, and kept `HasExpiredTokens()` lock-free.
- Refactored `SitecoreTokenService` internal OAuth2 DTOs (`AuthRequest`/`AuthResponse`) to `private readonly record struct` types, added cached pre-cache `AuthTokenUrl` validation (`ValidateAuthTokenUrl`) plus `default(SitecoreAuthClientCredentials)` and whitespace access-token guards, and optimized dynamic-proxy cache detection.
- Added `ConditionalWeakTable`-backed per-`(IServiceCollection, TClient)` builder factory holder sharing (`TypedHttpClientFactoryHolder`) without registering builder instances in DI, `AssemblyQualifiedName`-based client/options isolation, and default `SocketsHttpHandler.PooledConnectionLifetime` (2 minutes) on initial Singleton registrations.
- Dropped out-of-support `.NET 8.0` (`net8.0`) and `.NET 9.0` (`net9.0`) target frameworks across library, test, benchmark, and CI/CD pipelines while preserving historical `.NET 8.0` and `.NET 9.0` benchmark metrics in `README.md`.

---

## [1.0.0] - 2026-11-09
### Summary
- Official 1.0.0 production release featuring .NET 8/9/10 multi-targeting, `Microsoft.Extensions.Http.Resilience` integration, `CancellationToken` overloads, security hardening, high-performance `PriorityQueue` cache eviction, and comprehensive BenchmarkDotNet performance test suites.

### Added
- Multi-targeting support for `.NET 8.0`, `.NET 9.0`, and `.NET 10.0`.
- Native resilience pipeline integration via `Microsoft.Extensions.Http.Resilience` with lifetime-preserving `AddSitecoreAuthentication` and `AddSitecoreAuthenticationSingleton` dependency injection extensions.
- Cancellation token overloads on `ISitecoreTokenService` (`GetSitecoreAuthToken`, `TryRefreshSitecoreAuthToken`) with `ConfigureAwait(false)` for non-blocking asynchronous execution.
- Performance benchmark suite (`TokenCacheBenchmarks`) using `BenchmarkDotNet` across all target frameworks.
- Automated CI/CD release pipeline using NuGet OIDC Trusted Publishing and automated GitHub Release generation.

### Security
- Sensitive token masking in `SitecoreAuthToken.ToString()` to prevent inadvertent credential leakage in logs.
- Validation of `AuthTokenUrl` enforcing HTTPS or loopback HTTP schemes and rejecting URIs containing embedded user information (`UserInfo`).
- Validation of token response `ExpiresIn` ensuring positive expiration intervals.

### Performance & Reliability
- PriorityQueue-based size-bounded eviction (`O(N log K)`) in `SitecoreTokenCache` reducing allocation and CPU overhead during cache saturation.
- Atomic `TryRemove(KeyValuePair)` in token cleanup to guarantee thread-safe removal counts under concurrent access.
- Central Package Management with transitive package pinning enabled across all solution projects.

---

## [0.9.2] - 2025-08-16
### Summary
- Structure Updates

## [0.9.1] - 2025-08-15
### Summary
- Robust structured logging added across the library (service and cache).
- Expanded unit and integration tests for logging and caching behavior.
- Internal refactoring to reduce cyclomatic complexity and use early returns.

### Added
- Structured logging (message templates) in SitecoreTokenService with additional Debug, Warning, and Error messages.
- Structured logging in SitecoreTokenCache for cache hit/miss, expired tokens, eviction, cleanup, clear, and removal events.
- In-memory logger fixture and logging integration tests validating emitted messages.
- Unit tests for cache logging paths (hit, miss, expired, eviction, cleanup, clear, removal).

### Changed
- SitecoreTokenService refactored to favor early returns and include SafeReadBodyAsync for improved diagnostics.
- SitecoreTokenCache refactored to reduce nesting, add early returns, and keep cleanup opportunistic with sampling.
- README updated with logging documentation, badges, and quick commands for logging-specific tests.

---

## [0.9.0] - Initial Release
### Added
- First public release of Sitecore API Authorization library
- Provides OAuth2 client credentials flow for Sitecore Cloud
- Intelligent token caching and automatic refresh
- Thread-safe, high-performance implementation
- Dependency injection support for .NET
- Comprehensive integration and unit tests

---

This project enables secure, robust authentication for Sitecore Cloud APIs, with modern .NET best practices and open-source CI/CD support.
