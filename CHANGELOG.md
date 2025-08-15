# Changelog

All notable changes to this project will be documented in this file.

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
