# Changelog

All notable changes to this project will be documented in this file.

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
