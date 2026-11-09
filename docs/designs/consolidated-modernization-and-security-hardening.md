# Consolidated Modernization, Regression Recovery, and Security Hardening

*   **Author:** Aaron Metz (`acmetz`)
*   **Status:** Approved
*   **Date:** 2026-11-09

--------------------------------------------------------------------------------

## Overview

This design specifies the consolidation of six change streams in `Sitecore.API.Foundation.Authorization` into a single backward-compatible pull request targeting `main`. It recovers code accidentally reverted on `main` during a prior bot merge conflict resolution, integrates pending CI/CD workflow verification tests, adds backward-compatible `CancellationToken` and HTTP resilience support, folds in valid code-health fixes from open pull requests, migrates the solution to `.slnx`, and closes security and concurrency gaps in token caching and OAuth endpoint handling.

## The Problem

Automated maintenance and parallel branch work across `Sitecore.API.Foundation.Authorization` created four operational and technical problems:

1. **Silent Regression on `main` (`f986f15`)**: When PR #30 (`⚡ Optimize token cache eviction with PriorityQueue`) resolved merge conflicts against an outdated branch base, it overwrote `main` and reverted ~502 lines from 8 previously merged pull requests (#16, #22, #24, #25, #27, #28, #29, and #31), while committing a stray `commit_msg.txt` file. As a result, `main` currently logs raw OAuth error responses in cleartext (reintroducing the vulnerability fixed in PR #29), exposes `ClientSecret` in [`SitecoreAuthClientCredentials.ToString()`](../../src/Sitecore.API.Foundation.Authorization/Models/SitecoreAuthClientCredentials.cs) (reintroducing the vulnerability fixed in PR #22), lacks the `Benchmarks` project and exception unit tests, and uses unoptimized LINQ allocations during cache cleanup and token removal.
2. **Conflicting Open Pull Requests (`#17`–`#21`, `#26`)**: Six automated pull requests remain open with overlapping conflict-resolution commits that introduce `.orig` and `.patch` artifacts, retain baseline NuGet vulnerability warning suppressions (`NU1902;NU1903;NU1904` in [`Directory.Build.props`](../../Directory.Build.props) and `NU1902` in [`Sitecore.API.Foundation.Authorization.IntegrationTests.csproj`](../../tests/Sitecore.API.Foundation.Authorization.IntegrationTests/Sitecore.API.Foundation.Authorization.IntegrationTests.csproj)) instead of pinning vulnerable transitive packages, or introduce redundant namespace qualifications (such as PR #26 replacing `using System;` with fully-qualified `System.IDisposable`).
3. **Unmerged CI/CD & Async/Resilience Branches**:
   * Commit `c711286` on `feat/nuget-trusted-publishing` (containing xUnit workflow contract tests in [`GitHubWorkflowTests.cs`](../../tests/Sitecore.API.Foundation.Tests/GitHubWorkflowTests.cs), [`.actrc`](../../.actrc), the `actionlint` job, and a fix for the `dotnet test` filter that previously skipped unit tests in CI) was not included before PR #32 merged. Additionally, both [`.github/workflows/pr-check.yml`](../../.github/workflows/pr-check.yml) and [`.github/workflows/nuget-release.yml`](../../.github/workflows/nuget-release.yml) pass `-p:TreatWarningsAsErrors=false`, bypassing `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in [`Directory.Build.props`](../../Directory.Build.props).
   * Branch `origin/feature/asyn-updates-and-reliability` contains `CancellationToken`, `ConfigureAwait(false)`, and `Microsoft.Extensions.Http.Resilience` support, but its current implementation breaks constructor compatibility on [`SitecoreTokenService`](../../src/Sitecore.API.Foundation.Authorization/Services/SitecoreTokenService.cs), alters DI service lifetimes, and reintroduces raw HTTP response logging.
4. **Unaddressed Security, Concurrency, and Release Configuration Gaps**:
   * [`SitecoreAuthToken`](../../src/Sitecore.API.Foundation.Authorization/Models/SitecoreAuthToken.cs) is a `readonly record struct` without a custom `ToString()` override, causing its compiler-synthesized `ToString()` to output the raw `AccessToken` bearer value.
   * [`SitecoreTokenCache`](../../src/Sitecore.API.Foundation.Authorization/Services/SitecoreTokenCache.cs) removes entries by key (`_tokens.TryRemove(key, out _)`) rather than `KeyValuePair`, allowing concurrent cleanup or removal to delete a freshly updated token, and computes removal counts via `_tokens.Count` snapshots that race with concurrent mutations.
   * [`SitecoreTokenService`](../../src/Sitecore.API.Foundation.Authorization/Services/SitecoreTokenService.cs) does not validate that `AuthTokenUrl` uses HTTPS (or loopback HTTP for local testing) without embedded `UserInfo` credentials or that `expires_in` is positive.
   * GitHub repository `acmetz/Sitecore.API.Foundation.Authorization` lacks the `release` environment (`v*` tag restriction) and `NUGET_USER` secret required by [`nuget-release.yml`](../../.github/workflows/nuget-release.yml).

## Goals

*   **Fold in Commit `c711286` & Enforce CI Warnings as Errors**: Integrate [`GitHubWorkflowTests.cs`](../../tests/Sitecore.API.Foundation.Tests/GitHubWorkflowTests.cs), [`.actrc`](../../.actrc), the `lint-workflows` (`actionlint`) job in [`pr-check.yml`](../../.github/workflows/pr-check.yml), and the corrected `--filter "FullyQualifiedName!~TokenServiceIntegrationTests"` test filter in both [`pr-check.yml`](../../.github/workflows/pr-check.yml) and [`nuget-release.yml`](../../.github/workflows/nuget-release.yml), and remove `-p:TreatWarningsAsErrors=false` from both workflows.
*   **Restore All 8 Reverted PRs from `f986f15` & Migrate Solution to `.slnx`**: Recover PR #16 (`Benchmarks` project and [`TokenServiceIntegrationTests.cs`](../../tests/Sitecore.API.Foundation.Authorization.IntegrationTests/Tests/TokenServiceIntegrationTests.cs)), PR #22 (`SitecoreAuthClientCredentials.ToString()` masking and tests), PR #24 (`RemoveToken` `foreach` loop), PR #25 (`CloneResponseAsync` in [`SitecoreTokenServiceTests.cs`](../../tests/Sitecore.API.Foundation.Tests/SitecoreTokenServiceTests.cs)), PR #27 (`SitecoreAuthExceptionTests`), PR #28 (`ExecuteCleanupUnsafe` deduplication), PR #29 (raw OAuth body log removal and [`README.md`](../../README.md) alignment), and PR #31 (`CleanupExpiredTokensUnsafe` without `.ToList()`), while keeping PR #30's `PriorityQueue` cache eviction, migrating `Sitecore.API.Foundation.Authorization.sln` to [`Sitecore.API.Foundation.Authorization.slnx`](../../Sitecore.API.Foundation.Authorization.slnx), and deleting `commit_msg.txt`.
*   **Deliver Backward-Compatible Async and Resilience Support**: Add `CancellationToken` overloads, `ConfigureAwait(false)`, and `Microsoft.Extensions.Http.Resilience` configuration from `origin/feature/asyn-updates-and-reliability` while preserving existing 1-parameter interface methods, constructor signatures, `protected readonly HttpClient HttpClient`, and `Scoped`/`Singleton` DI lifetimes.
*   **Consolidate and Close Open Bot PRs**: Incorporate valid code-health changes from PRs #17, #18, #19, #20, and #21, reject PR #26, close all 6 open PRs (#17–#21, #26), and delete stale remote branches.
*   **Harden Security, Concurrency, and Package Hygiene**: Mask `AccessToken` in [`SitecoreAuthToken.ToString()`](../../src/Sitecore.API.Foundation.Authorization/Models/SitecoreAuthToken.cs), use atomic `_tokens.TryRemove(kvp)` with exact removal counting in [`SitecoreTokenCache`](../../src/Sitecore.API.Foundation.Authorization/Services/SitecoreTokenCache.cs), validate `AuthTokenUrl` (HTTPS or loopback HTTP, no `UserInfo`) and `expires_in > 0` in [`SitecoreTokenService`](../../src/Sitecore.API.Foundation.Authorization/Services/SitecoreTokenService.cs), and resolve `NU1901`–`NU1904` transitive package vulnerabilities in [`Directory.Packages.props`](../../Directory.Packages.props) and [`Directory.Build.props`](../../Directory.Build.props) with zero NuGet vulnerability warnings.
*   **Configure GitHub Release Environment**: Create the `release` environment on `acmetz/Sitecore.API.Foundation.Authorization` restricted to `v*` tags and configure the `NUGET_USER=acmetz` secret.

## Non-Goals

*   Introducing breaking changes to existing public method signatures, constructor parameter orders, or default DI lifetimes (`Scoped` for `AddSitecoreAuthentication`, `Singleton` for `AddSitecoreAuthenticationSingleton`).
*   Adding external distributed cache providers (such as Redis) or changing the OAuth2 `client_credentials` wire payload.
*   Modifying the Keycloak container realm schema in [`test-realm.json`](../../tests/Sitecore.API.Foundation.Authorization.IntegrationTests/Realm/test-realm.json).

## Open Questions

*   None.

## Critical User Journeys (CUJs)

### CUJ 1: Consumer Requests or Refreshes a Token with Optional Cancellation and Resilience

*   **Given**: A .NET 8, 9, or 10 application registers Sitecore authentication via `services.AddSitecoreAuthentication(...)` or `services.AddSitecoreAuthenticationSingleton(...)` (with optional `HttpStandardResilienceOptions` configuration).
*   **When**: The consumer calls `GetSitecoreAuthToken(credentials)` or `GetSitecoreAuthToken(credentials, cancellationToken)` on `ISitecoreTokenService`.
*   **Then**: Valid cached tokens are returned immediately; on a cache miss, `SitecoreTokenService` validates `AuthTokenUrl` (before emitting request logs), requests a token using the configured `HttpClient` pipeline, verifies `expires_in > 0`, caches the token, and never logs raw HTTP payloads, client secrets, or bearer tokens even if formatting or authentication fails.

### CUJ 2: Contributor Runs CI Checks and Publishes a Tagged NuGet Release

*   **Given**: The consolidated pull request is opened or a `v*` release tag is pushed to `acmetz/Sitecore.API.Foundation.Authorization`.
*   **When**: GitHub Actions (or local `act -W .github/workflows/pr-check.yml pull_request`) executes the workflow jobs without `-p:TreatWarningsAsErrors=false`.
*   **Then**: `actionlint` validates all workflows, `dotnet restore` and `dotnet build` succeed with zero `NU1901`–`NU1904` warnings across `net8.0`, `net9.0`, and `net10.0`, all unit and mock integration tests (including `GitHubWorkflowTests`, `SitecoreAuthClientCredentialsTests`, and `SitecoreAuthExceptionTests`) execute and pass, and `v*` tag pushes authenticate against the protected `release` environment using `NUGET_USER`.

## Technical Plan (Architecture)

### System Data Flow & Component Architecture

```mermaid
flowchart TD
    Consumer["Application / Consumer"] -->|"GetSitecoreAuthToken / TryRefreshSitecoreAuthToken"| IService["ISitecoreTokenService (1-arg & 2-arg CancellationToken overloads)"]
    IService --> Service["SitecoreTokenService (Scoped or Singleton)"]
    Service -->|"1. GetToken / RemoveToken(token) -> Credentials / SetToken"| Cache["SitecoreTokenCache (Singleton ConcurrentDictionary)"]
    Cache -->|"Opportunistic / Explicit Cleanup"| Cleanup["ExecuteCleanupUnsafe & EvictOldestTokensUnsafe (PriorityQueue + TryRemove(kvp))"]
    Service -->|"2. Validate AuthTokenUrl (HTTPS or Loopback HTTP, no UserInfo)"| UrlCheck["URI & Scheme Validator"]
    UrlCheck -->|"3. POST /oauth/token"| Http["HttpClient (via IHttpClientFactory + Optional StandardResilienceHandler)"]
    Http -->|"4. Deserialize AuthResponse & Verify ExpiresIn > 0"| TokenModel["SitecoreAuthToken (Masked ToString)"]
```

### 1. Backward-Compatible Async and Cancellation Contract

*   **Interface Overloads ([`ISitecoreTokenService.cs`](../../src/Sitecore.API.Foundation.Authorization/Abstractions/ISitecoreTokenService.cs))**:
    *   Remove the unused `using System;` directive (PR #17) and add `using System.Threading;`.
    *   Retain the existing 1-parameter signatures:
        *   `Task<SitecoreAuthToken> GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials);`
        *   `Task<SitecoreAuthToken> TryRefreshSitecoreAuthToken(SitecoreAuthToken token);`
    *   Add explicit 2-parameter overloads without default parameter values on the interface (preventing `CS0121` overload ambiguity while keeping binary and source compatibility):
        *   `Task<SitecoreAuthToken> GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials, CancellationToken cancellationToken);`
        *   `Task<SitecoreAuthToken> TryRefreshSitecoreAuthToken(SitecoreAuthToken token, CancellationToken cancellationToken);`
*   **Service Implementation ([`SitecoreTokenService.cs`](../../src/Sitecore.API.Foundation.Authorization/Services/SitecoreTokenService.cs))**:
    *   Preserve `protected readonly HttpClient HttpClient;` and the existing 4-parameter constructor signature `(HttpClient httpClient, ISitecoreTokenCache tokenCache, IOptions<SitecoreTokenServiceOptions> options, ILogger<SitecoreTokenService>? logger = null)`.
    *   Implement the 1-parameter methods by delegating to the 2-parameter methods with `CancellationToken.None`.
    *   In the 2-parameter methods, if `cancellationToken.IsCancellationRequested` is true, throw `new TaskCanceledException(null, null, cancellationToken)`; pass `cancellationToken` to `HttpClient.PostAsJsonAsync` and `response.Content.ReadFromJsonAsync`, filter the JSON deserialization `catch` block with `when (ex is not OperationCanceledException)`, and append `.ConfigureAwait(false)` to all awaited calls.

### 2. DI Registration Deduplication and HTTP Resilience

*   **Overload Preservation & Deduplication ([`ServiceCollectionExtensions.cs`](../../src/Sitecore.API.Foundation.Authorization/DependencyInjection/ServiceCollectionExtensions.cs))**:
    *   Keep the original 4 public extension method parameter-type signatures (`AddSitecoreAuthentication` and `AddSitecoreAuthenticationSingleton` with `Action<SitecoreTokenServiceOptions>?` or `IConfiguration, string`) without compile-time default parameter values as binary-compatible forwarding methods, and keep default parameter values on the 4 extended overloads so 0-argument and 1-argument source calls resolve without `CS0121` ambiguity.
    *   Add extended overloads accepting `Action<HttpStandardResilienceOptions>? configureResilience = null`, `Action<IHttpClientBuilder>? configureClient = null`, and `Func<IServiceProvider, HttpClient>? httpClientFactory = null`.
    *   Consolidate all registration paths into a single private `AddCoreServices` helper (resolving PR #21 without any `.orig`/`.patch` files).
*   **Lifetime-Preserving Resilience Wiring**:
    *   Register `ISitecoreTokenCache` via `services.TryAddSingleton<ISitecoreTokenCache, SitecoreTokenCache>()`.
    *   Configure a named `HttpClient` (`Constants.SitecoreHttpClientName`) via `services.AddHttpClient(Constants.SitecoreHttpClientName)`. When `configureResilience` is provided, attach `httpClientBuilder.AddStandardResilienceHandler().Configure(configureResilience)`. Invoke `configureClient?.Invoke(httpClientBuilder)`.
    *   Register `ISitecoreTokenService` using `services.TryAdd(new ServiceDescriptor(..., lifetime))` so pre-existing user registrations are respected (`ServiceCollectionExtensionsOverrideTests`) and the descriptor retains the exact `ServiceLifetime.Scoped` or `ServiceLifetime.Singleton` lifetime (`ServiceCollectionExtensionsTests`). When resolving `HttpClient`, use `httpClientFactory != null ? httpClientFactory(sp) : sp.GetRequiredService<IHttpClientFactory>().CreateClient(Constants.SitecoreHttpClientName)`.

### 3. Security Hardening, Secret Masking, and Validation

*   **Credential & Token Masking**:
    *   In [`SitecoreAuthClientCredentials.cs`](../../src/Sitecore.API.Foundation.Authorization/Models/SitecoreAuthClientCredentials.cs), restore PR #22's `public override string ToString() => $"SitecoreAuthClientCredentials {{ ClientId = {ClientId}, ClientSecret = *** }}";`.
    *   In [`SitecoreAuthToken.cs`](../../src/Sitecore.API.Foundation.Authorization/Models/SitecoreAuthToken.cs), add `public override string ToString() => $"SitecoreAuthToken {{ AccessToken = ***, Expiration = {Expiration} }}";` so bearer tokens are never emitted when formatting the struct.
*   **Removal of Raw OAuth Payload Logging (PR #29) & Documentation Alignment**:
    *   In [`SitecoreTokenService.cs`](../../src/Sitecore.API.Foundation.Authorization/Services/SitecoreTokenService.cs), delete `SafeReadBodyAsync` and remove `{Body}` and `{Raw}` parameters from `_logger?.LogWarning` and `_logger?.LogError` calls.
    *   In [`README.md`](../../README.md), remove `Body: <captured>` and `Raw: <captured>` from the `## Logging` examples and update the CI workflow (`pr-check.yml`) and `.NET 8 | 9 | 10` badges.
*   **C# Naming & Constant Consolidation (PR #19 & PR #20)**:
    *   In [`SitecoreTokenService.cs`](../../src/Sitecore.API.Foundation.Authorization/Services/SitecoreTokenService.cs), rename `AuthResponse.access_token` to `AccessToken` (`[JsonPropertyName("access_token")]`) and `expires_in` to `ExpiresIn` (`[JsonPropertyName("expires_in")]`).
    *   In [`SitecoreTokenServiceOptions.cs`](../../src/Sitecore.API.Foundation.Authorization/Configuration/SitecoreTokenServiceOptions.cs), initialize `AuthTokenUrl` to `Constants.SitecoreCloudAuthTokenUrl`.
*   **Endpoint (`AuthTokenUrl`) and Token Lifetime (`ExpiresIn`) Validation**:
    *   Inside `GetSitecoreAuthToken` (immediately after cache miss, before logging `"Requesting new token..."` or calling `HttpClient.PostAsJsonAsync`), validate that `_options.AuthTokenUrl` parses via `Uri.TryCreate(_options.AuthTokenUrl, UriKind.Absolute, out var requestUri)`, contains no embedded credentials (`string.IsNullOrEmpty(requestUri.UserInfo)`), and satisfies `requestUri.Scheme == Uri.UriSchemeHttps || (requestUri.Scheme == Uri.UriSchemeHttp && requestUri.IsLoopback)`. If validation fails, log a warning (without emitting raw `UserInfo`) and throw `SitecoreAuthException`.
    *   After deserializing `AuthResponse`, verify `result.ExpiresIn > 0`; if `<= 0`, log an error and throw `SitecoreAuthResponseException`.

### 4. Atomic Cache Removal & Allocation Optimization

*   **Atomic `KeyValuePair` Removal & Exact Counting ([`SitecoreTokenCache.cs`](../../src/Sitecore.API.Foundation.Authorization/Services/SitecoreTokenCache.cs))**:
    *   **`RemoveToken` (PR #24 + Atomic Concurrency Fix)**: Replace `_tokens.FirstOrDefault(...)` with a `foreach (var kvp in _tokens)` loop. When `kvp.Value.Equals(token)` matches, call `_tokens.TryRemove(kvp)` so removal succeeds only if the entry still maps to `token`.
    *   **`ExecuteCleanupUnsafe` & `CleanupExpiredTokensUnsafe` (PR #28 & PR #31 + Exact Count)**: Deduplicate cleanup tracking in `PerformCleanup` and `TryPerformCleanup` into `ExecuteCleanupUnsafe()`. In `CleanupExpiredTokensUnsafe()`, iterate `foreach (var kvp in _tokens)` without `.ToList()`, call `_tokens.TryRemove(kvp)` when `kvp.Value.IsExpired` is true, and return the exact `int` count of entries where `_tokens.TryRemove(kvp)` returned `true` (replacing `before - _tokens.Count`).
    *   **`EvictOldestTokensUnsafe` (PR #30 + Atomic Concurrency Fix & Exact Count)**: Retain the size-bounded max-heap `PriorityQueue`, storing `KeyValuePair<SitecoreAuthClientCredentials, SitecoreAuthToken>` with priority `kvp.Value.Expiration.UtcTicks`, dequeue into `_tokens.TryRemove(kvp)`, and return the exact `int` count of entries where `_tokens.TryRemove(kvp)` returned `true` (replacing `before - _tokens.Count` in `TryEvictOldestTokens`).

### 5. CI/CD Workflows, Package Hygiene, and Benchmarks/Tests Recovery

*   **Workflow Contract Tests, Warning Gate, & `act` Support (`c711286`)**:
    *   Include [`.actrc`](../../.actrc), [`GitHubWorkflowTests.cs`](../../tests/Sitecore.API.Foundation.Tests/GitHubWorkflowTests.cs), the `lint-workflows` (`actionlint`) job in [`.github/workflows/pr-check.yml`](../../.github/workflows/pr-check.yml), and the `--filter "FullyQualifiedName!~TokenServiceIntegrationTests"` filter in [`.github/workflows/pr-check.yml`](../../.github/workflows/pr-check.yml) and [`.github/workflows/nuget-release.yml`](../../.github/workflows/nuget-release.yml).
    *   Remove `-p:TreatWarningsAsErrors=false` from all `dotnet` CLI steps in [`.github/workflows/pr-check.yml`](../../.github/workflows/pr-check.yml) and [`.github/workflows/nuget-release.yml`](../../.github/workflows/nuget-release.yml), and assert in [`GitHubWorkflowTests.cs`](../../tests/Sitecore.API.Foundation.Tests/GitHubWorkflowTests.cs) that neither workflow disables `TreatWarningsAsErrors`.
*   **Reverted Test & Benchmark Restoration (`f986f15` Recovery) & `.slnx` Migration**:
    *   Restore `tests/Sitecore.API.Foundation.Authorization.Benchmarks/` (`Program.cs`, `Sitecore.API.Foundation.Authorization.Benchmarks.csproj`, `TokenCacheBenchmarks.cs`) (PR #16), migrate `Sitecore.API.Foundation.Authorization.sln` to [`Sitecore.API.Foundation.Authorization.slnx`](../../Sitecore.API.Foundation.Authorization.slnx) containing all 4 projects (`src/Sitecore.API.Foundation.Authorization`, `Sitecore.API.Foundation.Tests`, `Sitecore.API.Foundation.Authorization.IntegrationTests`, and `Sitecore.API.Foundation.Authorization.Benchmarks`), and delete the legacy `.sln` file.
    *   Restore [`TokenServiceIntegrationTests.cs`](../../tests/Sitecore.API.Foundation.Authorization.IntegrationTests/Tests/TokenServiceIntegrationTests.cs) (PR #16).
    *   Restore and merge `SitecoreAuthClientCredentialsTests.cs` (PR #18 & PR #22), `SitecoreAuthExceptionTests.cs` (PR #27), `CloneResponseAsync` in [`SitecoreTokenServiceTests.cs`](../../tests/Sitecore.API.Foundation.Tests/SitecoreTokenServiceTests.cs) (PR #25), and `ResilienceTests.cs` (`feature/asyn-updates-and-reliability`).
    *   Delete root `commit_msg.txt` and remove stray local file paths from [`Sitecore.API.Foundation.Tests.csproj`](../../tests/Sitecore.API.Foundation.Tests/Sitecore.API.Foundation.Tests.csproj).
*   **Central Package Management & Vulnerability Pinning**:
    *   In [`Directory.Packages.props`](../../Directory.Packages.props), add `<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>`, add `Microsoft.Extensions.Http.Resilience` (`10.0.0`), and update existing `<PackageVersion>` entries in place for `BouncyCastle.Cryptography` (`2.4.0` -> `2.6.2`), `SSH.NET` (`2024.2.0` -> `2026.0.0`), `Microsoft.SourceLink.GitHub` (`8.0.0` -> `10.0.403`), and `Microsoft.Build.Tasks.Git` (`9.0.0` -> `10.0.403`) so `dotnet restore` and `dotnet build` produce zero `NU1603` and `NU1901`–`NU1904` warnings after removing `NU1902;NU1903;NU1904` from [`Directory.Build.props`](../../Directory.Build.props) and `NU1902` from [`Sitecore.API.Foundation.Authorization.IntegrationTests.csproj`](../../tests/Sitecore.API.Foundation.Authorization.IntegrationTests/Sitecore.API.Foundation.Authorization.IntegrationTests.csproj).
*   **GitHub Environment & PR/Branch Cleanup**:
    *   Configure the `release` environment (`v*` tag policy) and `NUGET_USER=acmetz` secret on `acmetz/Sitecore.API.Foundation.Authorization`.
    *   Close superseded open PRs `#17`, `#18`, `#19`, `#20`, `#21`, and `#26` referencing the consolidated PR, and delete the 18 stale/merged remote branches on `origin` once the consolidated PR is merged into `main`.

## Alternatives Considered

### Alternative A: Merging Open Bot PRs (#17–#21, #26) Individually via GitHub

*   **Pros**: Preserves individual pull request attribution in the GitHub merge log without creating a new branch.
*   **Cons**: Multiple conflict-resolution commits on those branches introduced untracked `.orig` and `.patch` files, retained baseline `NU1902;NU1903;NU1904` warning suppressions instead of fixing vulnerable packages, and used stale base trees that risk overwriting recent `main` commits (as occurred when PR #30 reverted 8 prior PRs).
*   **Reason for Rejection**: Combining all valid changes into a single clean PR on top of `origin/main` avoids repeat merge regressions and eliminates untracked merge artifacts.

### Alternative B: Replacing `ISitecoreTokenService` Methods with a Single Optional `CancellationToken = default` Signature

*   **Pros**: Keeps only two methods on `ISitecoreTokenService` instead of four while remaining source-compatible for callers that recompile.
*   **Cons**: Breaks binary compatibility (`MissingMethodException`) for callers compiled against the 1-parameter signature and breaks existing test mocks or custom `ISitecoreTokenService` implementations that implement the 1-parameter signature.
*   **Reason for Rejection**: Keeping the 1-parameter overload alongside the 2-parameter `CancellationToken` overload (without `= default` on the interface to prevent `CS0121` ambiguity) preserves both binary and source compatibility.

### Alternative C: Adopting `origin/feature/asyn-updates-and-reliability` Service & DI Changes Verbatim

*   **Pros**: Requires no reconciliation work when merging the branch.
*   **Cons**: Reorders `SitecoreTokenService` constructor parameters, removes `protected readonly HttpClient HttpClient`, uses `AddHttpClient<ISitecoreTokenService, SitecoreTokenService>()` (which registers the service as `Transient` unless the registered `ServiceDescriptor` is manually replaced), and reintroduces `SafeReadBodyAsync` raw response logging removed in PR #29.
*   **Reason for Rejection**: Violates the backward-compatibility requirement, breaks `Scoped` DI lifetime tests, and reintroduces sensitive OAuth payload logging.

### Alternative D: Fully-Qualifying `System.IDisposable` and `System.TimeSpan` to Drop `using System;` (PR #26 & PR #23)

*   **Pros**: Removes `using System;` from two files.
*   **Cons**: `IDisposable` and `TimeSpan` are in the `System` namespace, so the import was not unused; fully-qualifying BCL types in declarations diverges from the codebase's style convention.
*   **Reason for Rejection**: Provides no code-health benefit and degrades readability (flagged in [PR #26 review](https://github.com/acmetz/Sitecore.API.Foundation.Authorization/pull/26#pullrequestreview-2379484873)).

### Alternative E: Validating `AuthTokenUrl` in the `SitecoreTokenService` Constructor or Rejecting All `http://` URIs

*   **Pros**: Fails early at service construction time and blocks all cleartext HTTP endpoints.
*   **Cons**: Constructor validation breaks existing unit tests that construct `SitecoreTokenService` in `Arrange` and assert that `GetSitecoreAuthToken` throws in `Act` (`SitecoreTokenServiceTests.GetSitecoreAuthToken_WithInvalidAuthUrl_ShouldThrowException`), and blocking loopback `http://` breaks local OAuth integration tests (`MockIntegrationTests`, `LoggingIntegrationTests`, and `TokenServiceIntegrationTests`).
*   **Reason for Rejection**: Validating `AuthTokenUrl` inside `GetSitecoreAuthToken` while allowing `https://` and loopback `http://` (`uri.IsLoopback`) enforces transport security for remote endpoints while keeping unit and local integration tests working.

--------------------------------------------------------------------------------

## Non-Functional Requirements

*   **Security & Privacy**:
    *   `SitecoreAuthClientCredentials.ToString()` and `SitecoreAuthToken.ToString()` must mask `ClientSecret` and `AccessToken` as `***`.
    *   `SitecoreTokenService` must never log raw HTTP response bodies on authentication or JSON deserialization failure.
    *   `AuthTokenUrl` must reject malformed URIs, URIs with embedded `UserInfo` credentials, and non-loopback `http://` URIs before logging request initiation or transmitting credentials.
    *   `dotnet restore` and `dotnet build` must pass with zero `NU1901`–`NU1904` NuGet vulnerability warnings across all projects without `-p:TreatWarningsAsErrors=false`.
*   **Performance & Concurrency**:
    *   `SitecoreTokenCache` must avoid LINQ `.ToList()` and `.FirstOrDefault()` allocations in `CleanupExpiredTokensUnsafe` and `RemoveToken`, use `PriorityQueue` (`O(N log K)`) for max-size eviction, and use atomic `_tokens.TryRemove(kvp)` with exact removal counting to prevent race conditions during concurrent token updates.
*   **Backward Compatibility**:
    *   All existing public constructors, `protected` members, extension method signatures, and DI service lifetimes (`Scoped` and `Singleton`) must remain source- and binary-compatible across `net8.0`, `net9.0`, and `net10.0`.

## Risks & Mitigations

*   **Singleton `SitecoreTokenService` and `HttpClient` Handler Lifetime**: When registered via `AddSitecoreAuthenticationSingleton`, `SitecoreTokenService` resolves and stores `HttpClient` once from `IHttpClientFactory.CreateClient(Constants.SitecoreHttpClientName)`. Consumers requiring periodic handler rotation can either register via `AddSitecoreAuthentication` (`Scoped`) or configure `SocketsHttpHandler.PooledConnectionLifetime` via the `configureClient` callback on `IHttpClientBuilder`.
*   **Concurrent Cache-Miss Stampede Under Resilience Retries**: Until per-credential in-flight request coalescing is added (`Future Improvements`), simultaneous cache misses for the same `SitecoreAuthClientCredentials` during an OAuth endpoint outage execute independent retry pipelines ($N_{\text{callers}} \times \text{MaxRetryAttempts}$). Mitigated in steady state by in-memory token caching and bounded retry limits in `HttpStandardResilienceOptions`.
*   **Opportunistic Cleanup/Eviction Lock Timeouts Under Contention**: `TryPerformCleanup` (`10ms` timeout) and `TryEvictOldestTokens` (`50ms` timeout) skip execution if another thread holds `_cleanupLock`, allowing `_tokens.Count` to briefly exceed `MaxCacheSize` under burst writes until the next `SetToken` call or explicit `PerformCleanup()`.
*   **Loopback `http://` Exception in `AuthTokenUrl` Validation**: Permitting `http://` only when `requestUri.IsLoopback` is true supports local test fixtures (`MockOAuth2TestFixture` and Testcontainers Keycloak) while blocking plaintext credential transmission over non-loopback networks.

--------------------------------------------------------------------------------

## Additional Resources

*   Open and merged pull requests in [acmetz/Sitecore.API.Foundation.Authorization](https://github.com/acmetz/Sitecore.API.Foundation.Authorization/pulls) (#16–#32)
*   Unmerged branches `origin/feat/nuget-trusted-publishing` (`c711286`) and `origin/feature/asyn-updates-and-reliability` (`af270db`)

## Future Improvements

*   **In-Flight Token Request Coalescing**: Deduplicate concurrent cache-miss HTTP requests for the same `SitecoreAuthClientCredentials` using per-credential async coordination so parallel callers share a single in-flight OAuth request.
*   **Proactive Token Expiration Buffer**: Add a configurable clock-skew / refresh buffer (e.g., 30–60 seconds before `Expiration`) to `SitecoreAuthToken` or `SitecoreTokenServiceOptions` so tokens close to expiry are refreshed before downstream API calls reject them.

--------------------------------------------------------------------------------

## Detailed Implementation Plan

> [!NOTE]
> All `(Create)`, `(Modify)`, and `(Delete)` labels below are relative to `origin/main` (`6aa2c4b`). When branching from `origin/main` and cherry-picking commit `c711286` from `feat/nuget-trusted-publishing`, items from `c711286` (`.actrc`, `GitHubWorkflowTests.cs`, and the initial `pr-check.yml` / `nuget-release.yml` test filter updates) are brought onto the consolidated branch first and then refined in Phase 4.

### Phase 1: Package Hygiene, Build Props, Benchmark Recovery, & `.slnx` Migration

#### 1. `commit_msg.txt` (Delete)

*   **Type / Scope**: Repository Hygiene
*   **Proposed Changes**: Delete `commit_msg.txt` from git tracking (`git rm commit_msg.txt`).
*   **Rationale**: Accidentally committed to the repository root during PR #30 (`f986f15`) conflict resolution.

#### 2. `tests/Sitecore.API.Foundation.Authorization.Benchmarks/Sitecore.API.Foundation.Authorization.Benchmarks.csproj`, `Program.cs`, & `TokenCacheBenchmarks.cs` (Create)

*   **Type / Scope**: Benchmark Suite Recovery (Restored from PR #16 `5658b10`)
*   **Proposed Changes**: Restore the three benchmark files verbatim via `git checkout 5658b10 -- tests/Sitecore.API.Foundation.Authorization.Benchmarks/`.
*   **Rationale**: Restores the `BenchmarkDotNet` project and `SitecoreTokenCache` benchmarks (`net8.0;net9.0;net10.0`) deleted in `f986f15`.

#### 3. `Sitecore.API.Foundation.Authorization.sln` (Delete) & [`Sitecore.API.Foundation.Authorization.slnx`](../../Sitecore.API.Foundation.Authorization.slnx) (Create)

*   **Type / Scope**: XML Solution File Migration
*   **Proposed Changes**: Add `tests/Sitecore.API.Foundation.Authorization.Benchmarks/Sitecore.API.Foundation.Authorization.Benchmarks.csproj` under the `/tests/` solution folder, migrate via `dotnet sln migrate` to `Sitecore.API.Foundation.Authorization.slnx` with `<Folder Name="/src/">` containing `src/Sitecore.API.Foundation.Authorization/Sitecore.API.Foundation.Authorization.csproj` and `<Folder Name="/tests/">` containing `Sitecore.API.Foundation.Tests.csproj`, `Sitecore.API.Foundation.Authorization.IntegrationTests.csproj`, and `Sitecore.API.Foundation.Authorization.Benchmarks.csproj`, and delete `Sitecore.API.Foundation.Authorization.sln`.
*   **Rationale**: Recovers the benchmark project entry deleted by PR #30 (`f986f15`) and migrates the solution file to the XML-based `.slnx` format supported by the .NET 10 SDK.

#### 4. [`Directory.Packages.props`](../../Directory.Packages.props) (Modify)

*   **Type / Scope**: Central Package Management
*   **Proposed Changes**: Enable `<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>` in `<PropertyGroup>`, add `<PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="10.0.0" />`, and update existing `<PackageVersion>` entries in place for `BouncyCastle.Cryptography` (`2.4.0` -> `2.6.2`), `SSH.NET` (`2024.2.0` -> `2026.0.0`), `Microsoft.SourceLink.GitHub` (`8.0.0` -> `10.0.403`), and `Microsoft.Build.Tasks.Git` (`9.0.0` -> `10.0.403`).
*   **Rationale**: Makes transitive vulnerability pins effective under Central Package Management without duplicate `<PackageVersion>` (`NU1506`) errors so `dotnet restore` and `dotnet build` produce zero `NU1603` and `NU1901`–`NU1904` warnings.

#### 5. [`Directory.Build.props`](../../Directory.Build.props) (Modify)

*   **Type / Scope**: Solution Build Configuration
*   **Proposed Changes**: Remove `NU1902;NU1903;NU1904` from `<WarningsNotAsErrors>`, leaving `<WarningsNotAsErrors>NU1603</WarningsNotAsErrors>`.
*   **Rationale**: Restores the build security gate so moderate, high, and critical NuGet package vulnerabilities (`NU1901`–`NU1904`) fail the build rather than being globally ignored.

#### 6. [`tests/Sitecore.API.Foundation.Authorization.IntegrationTests/Sitecore.API.Foundation.Authorization.IntegrationTests.csproj`](../../tests/Sitecore.API.Foundation.Authorization.IntegrationTests/Sitecore.API.Foundation.Authorization.IntegrationTests.csproj) (Modify)

*   **Type / Scope**: Integration Test Project File
*   **Proposed Changes**: Remove `<NoWarn>$(NoWarn);NU1902</NoWarn>` (retaining existing `<PackageReference>` items).
*   **Rationale**: Central transitive pinning in `Directory.Packages.props` resolves the underlying vulnerability without project-level warning suppression.

#### 7. [`tests/Sitecore.API.Foundation.Tests/Sitecore.API.Foundation.Tests.csproj`](../../tests/Sitecore.API.Foundation.Tests/Sitecore.API.Foundation.Tests.csproj) (Modify)

*   **Type / Scope**: Unit Test Project File
*   **Proposed Changes**: Remove the `<ItemGroup>` containing `<None Remove="C:\Users\ametz\source\repos\Sitecore.API.Foundation\LICENSE" />` and `<None Remove="C:\Users\ametz\source\repos\Sitecore.API.Foundation\README.md" />`.
*   **Rationale**: Removes machine-specific Windows paths from the project file.

*   **Phase 1 Verification Checkpoint**: Run `dotnet restore && dotnet build --configuration Release --no-restore` and verify 0 errors and 0 `NU1901`–`NU1904` warnings across all 4 projects in `Sitecore.API.Foundation.Authorization.slnx`.

---

### Phase 2: Core Library Models, Cache, Token Service, & DI Resilience

#### 8. [`src/Sitecore.API.Foundation.Authorization/Sitecore.API.Foundation.Authorization.csproj`](../../src/Sitecore.API.Foundation.Authorization/Sitecore.API.Foundation.Authorization.csproj) (Modify)

*   **Type / Scope**: Core Library Project File
*   **Proposed Changes**: Add `<PackageReference Include="Microsoft.Extensions.Http.Resilience" />`.
*   **Rationale**: Required by `ServiceCollectionExtensions` to support `HttpStandardResilienceOptions` and `AddStandardResilienceHandler()`.

#### 9. [`src/Sitecore.API.Foundation.Authorization/Abstractions/ISitecoreTokenService.cs`](../../src/Sitecore.API.Foundation.Authorization/Abstractions/ISitecoreTokenService.cs) (Modify)

*   **Type / Scope**: Public Service Abstraction
*   **Proposed Changes**: Remove unused `using System;` (PR #17), add `using System.Threading;`, keep the existing 1-parameter method declarations `GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials)` and `TryRefreshSitecoreAuthToken(SitecoreAuthToken token)`, and add 2-parameter overloads `GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials, CancellationToken cancellationToken)` and `TryRefreshSitecoreAuthToken(SitecoreAuthToken token, CancellationToken cancellationToken)` without default parameter values on the interface.
*   **Rationale**: Adds `CancellationToken` support from `origin/feature/asyn-updates-and-reliability` while avoiding `CS0121` call ambiguity and preserving binary and source compatibility for 1-argument callers.

#### 10. [`src/Sitecore.API.Foundation.Authorization/Configuration/Constants.cs`](../../src/Sitecore.API.Foundation.Authorization/Configuration/Constants.cs) (Modify)

*   **Type / Scope**: Internal Configuration Constants
*   **Proposed Changes**: Retain `SitecoreCloudAuthTokenUrl` and add `internal const string SitecoreHttpClientName = "Sitecore.API.Foundation.Authorization.HttpClient";`.
*   **Rationale**: Centralizes the default token endpoint URL and provides a dedicated named `HttpClient` key for DI and resilience registration.

#### 11. [`src/Sitecore.API.Foundation.Authorization/Configuration/SitecoreTokenServiceOptions.cs`](../../src/Sitecore.API.Foundation.Authorization/Configuration/SitecoreTokenServiceOptions.cs) (Modify)

*   **Type / Scope**: Public Options Model
*   **Proposed Changes**: Reference `Constants.SitecoreCloudAuthTokenUrl` as the default initializer for `AuthTokenUrl`.
*   **Rationale**: Resolves PR #20 by eliminating the duplicate hardcoded URL string and using the constant defined in `Constants.cs`.

#### 12. [`src/Sitecore.API.Foundation.Authorization/Models/SitecoreAuthClientCredentials.cs`](../../src/Sitecore.API.Foundation.Authorization/Models/SitecoreAuthClientCredentials.cs) (Modify)

*   **Type / Scope**: Public Credentials Record Struct
*   **Proposed Changes**: Restore `public override string ToString() => $"SitecoreAuthClientCredentials {{ ClientId = {ClientId}, ClientSecret = *** }}";` from PR #22 (`559e80a`).
*   **Rationale**: Prevents accidental exposure of `ClientSecret` in logs, traces, or exception messages when the record struct is formatted as a string.

#### 13. [`src/Sitecore.API.Foundation.Authorization/Models/SitecoreAuthToken.cs`](../../src/Sitecore.API.Foundation.Authorization/Models/SitecoreAuthToken.cs) (Modify)

*   **Type / Scope**: Public Token Record Struct
*   **Proposed Changes**: Add `public override string ToString() => $"SitecoreAuthToken {{ AccessToken = ***, Expiration = {Expiration} }}";`.
*   **Rationale**: Closes the security gap where the compiler-generated `readonly record struct` `ToString()` printed raw bearer tokens in cleartext.

#### 14. [`src/Sitecore.API.Foundation.Authorization/Services/SitecoreTokenCache.cs`](../../src/Sitecore.API.Foundation.Authorization/Services/SitecoreTokenCache.cs) (Modify)

*   **Type / Scope**: Token Cache Service
*   **Proposed Changes**:
    *   Remove unused `using System.Linq;`.
    *   In `RemoveToken`: replace `_tokens.FirstOrDefault(...)` with a `foreach (var kvp in _tokens)` loop (PR #24). When `kvp.Value.Equals(token)` is true, call `if (_tokens.TryRemove(kvp))` and on `true` log and return `kvp.Key` (continuing if a concurrent thread already mutated that entry).
    *   In `PerformCleanup` and `TryPerformCleanup`: extract duplicated cleanup counting, logging, and `LastCleanup` timestamp update into `ExecuteCleanupUnsafe()` (PR #28).
    *   In `CleanupExpiredTokensUnsafe`: change return type to `int`, iterate `foreach (var kvp in _tokens)` without `.ToList()` (PR #31), increment `removed` each time `kvp.Value.IsExpired && _tokens.TryRemove(kvp)` succeeds, and return `removed`.
    *   In `TryEvictOldestTokens` and `EvictOldestTokensUnsafe`: preserve the `PriorityQueue` max-heap eviction from PR #30 (`f986f15`), enqueueing `KeyValuePair<SitecoreAuthClientCredentials, SitecoreAuthToken>` items ordered by `kvp.Value.Expiration.UtcTicks`, changing `EvictOldestTokensUnsafe` to return the `int` count of successful `_tokens.TryRemove(kvp)` calls, and assigning `var removed = EvictOldestTokensUnsafe();` in `TryEvictOldestTokens()`.
*   **Rationale**: Restores PRs #24, #28, and #31 alongside PR #30 while eliminating TOCTOU race conditions and inaccurate `_tokens.Count` subtraction under concurrent writes.

#### 15. [`src/Sitecore.API.Foundation.Authorization/Services/SitecoreTokenService.cs`](../../src/Sitecore.API.Foundation.Authorization/Services/SitecoreTokenService.cs) (Modify)

*   **Type / Scope**: Token Acquisition Service
*   **Proposed Changes**:
    *   Preserve `protected readonly HttpClient HttpClient;` and the 4-parameter constructor `(HttpClient httpClient, ISitecoreTokenCache tokenCache, IOptions<SitecoreTokenServiceOptions> options, ILogger<SitecoreTokenService>? logger = null)`.
    *   Rename `AuthResponse` properties (`Models/AuthResponse.cs` or nested record) to `AccessToken` and `ExpiresIn` with `[JsonPropertyName("access_token")]` and `[JsonPropertyName("expires_in")]` attributes (PR #19).
    *   Remove `SafeReadBodyAsync` and remove `{Body}` and `{Raw}` response payload logging from warning/error logs (PR #29).
    *   Implement the 1-parameter methods by delegating to the 2-parameter (`CancellationToken`) overloads with `CancellationToken.None`.
    *   In the 2-parameter `GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials, CancellationToken cancellationToken)` and `TryRefreshSitecoreAuthToken(SitecoreAuthToken token, CancellationToken cancellationToken)`: if `cancellationToken.IsCancellationRequested` is true, throw `new TaskCanceledException(null, null, cancellationToken)`; pass `cancellationToken` to `HttpClient.PostAsJsonAsync` and `response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: cancellationToken)`, filter the JSON parse `catch` block with `catch (Exception ex) when (ex is not OperationCanceledException)`, and append `.ConfigureAwait(false)` to all awaited calls.
    *   Immediately after a cache miss (before logging `"Requesting new token..."`), validate `_options.AuthTokenUrl` via `Uri.TryCreate(_options.AuthTokenUrl, UriKind.Absolute, out var requestUri)`, `string.IsNullOrEmpty(requestUri.UserInfo)`, and `requestUri.Scheme == Uri.UriSchemeHttps || (requestUri.Scheme == Uri.UriSchemeHttp && requestUri.IsLoopback)`. On failure, log `_logger?.LogWarning("Invalid or insecure AuthTokenUrl configured for clientId {ClientId}. AuthTokenUrl must be an absolute HTTPS URI (or loopback HTTP URI) without embedded credentials.", credentials.ClientId)` and throw `new SitecoreAuthException("AuthTokenUrl must be an absolute HTTPS URI (or loopback HTTP URI) without embedded credentials.")`.
    *   After verifying `result` and `result.AccessToken`, verify `result.ExpiresIn > 0`; if `<= 0`, log `_logger?.LogError("Auth response contained non-positive expires_in ({ExpiresIn}) for clientId {ClientId}.", result.ExpiresIn, credentials.ClientId)` and throw `new SitecoreAuthResponseException($"Auth response contained non-positive expires_in ({result.ExpiresIn}).")`.
*   **Rationale**: Combines PR #19, PR #29, `feature/asyn-updates-and-reliability`, and endpoint/lifetime security validation without breaking existing callers or subclasses.

#### 16. [`src/Sitecore.API.Foundation.Authorization/DependencyInjection/ServiceCollectionExtensions.cs`](../../src/Sitecore.API.Foundation.Authorization/DependencyInjection/ServiceCollectionExtensions.cs) (Modify)

*   **Type / Scope**: Dependency Injection Registration
*   **Proposed Changes**:
    *   Provide 4 binary-compatible forwarding overloads **without** default parameter values (preventing `CS0121` ambiguity):
        1. `AddSitecoreAuthentication(this IServiceCollection services, Action<SitecoreTokenServiceOptions>? configureOptions)`
        2. `AddSitecoreAuthentication(this IServiceCollection services, IConfiguration configuration, string sectionName)`
        3. `AddSitecoreAuthenticationSingleton(this IServiceCollection services, Action<SitecoreTokenServiceOptions>? configureOptions)`
        4. `AddSitecoreAuthenticationSingleton(this IServiceCollection services, IConfiguration configuration, string sectionName)`
    *   Provide 4 extended overloads **with** default parameter values so 0-arg (`AddSitecoreAuthentication()`), 1-arg (`AddSitecoreAuthentication(configuration)`), and named/resilience calls resolve unambiguously:
        5. `AddSitecoreAuthentication(this IServiceCollection services, Action<SitecoreTokenServiceOptions>? configureOptions = null, Action<HttpStandardResilienceOptions>? configureResilience = null, Action<IHttpClientBuilder>? configureClient = null, Func<IServiceProvider, HttpClient>? httpClientFactory = null)`
        6. `AddSitecoreAuthentication(this IServiceCollection services, IConfiguration configuration, string sectionName = "SitecoreAuthentication", Action<HttpStandardResilienceOptions>? configureResilience = null, Action<IHttpClientBuilder>? configureClient = null, Func<IServiceProvider, HttpClient>? httpClientFactory = null)`
        7. `AddSitecoreAuthenticationSingleton(this IServiceCollection services, Action<SitecoreTokenServiceOptions>? configureOptions = null, Action<HttpStandardResilienceOptions>? configureResilience = null, Action<IHttpClientBuilder>? configureClient = null, Func<IServiceProvider, HttpClient>? httpClientFactory = null)`
        8. `AddSitecoreAuthenticationSingleton(this IServiceCollection services, IConfiguration configuration, string sectionName = "SitecoreAuthentication", Action<HttpStandardResilienceOptions>? configureResilience = null, Action<IHttpClientBuilder>? configureClient = null, Func<IServiceProvider, HttpClient>? httpClientFactory = null)`
    *   Consolidate registration logic into `private static IServiceCollection AddCoreServices(...)` (PR #21) registering `ISitecoreTokenCache` via `TryAddSingleton`, configuring `var httpClientBuilder = services.AddHttpClient(Constants.SitecoreHttpClientName)` (attaching `httpClientBuilder.AddStandardResilienceHandler().Configure(configureResilience)` when `configureResilience is not null` and invoking `configureClient?.Invoke(httpClientBuilder)`), and registering `ISitecoreTokenService` via `services.TryAdd(new ServiceDescriptor(typeof(ISitecoreTokenService), sp => new SitecoreTokenService(httpClientFactory != null ? httpClientFactory(sp) : sp.GetRequiredService<IHttpClientFactory>().CreateClient(Constants.SitecoreHttpClientName), sp.GetRequiredService<ISitecoreTokenCache>(), sp.GetRequiredService<IOptions<SitecoreTokenServiceOptions>>(), sp.GetService<ILogger<SitecoreTokenService>>()), lifetime))`.
*   **Rationale**: Deduplicates DI registration (PR #21) and integrates `Microsoft.Extensions.Http.Resilience` while avoiding `CS0121` and preserving exact `Scoped`/`Singleton` lifetimes, optional `ILogger` resolution, and custom service overrides.

*   **Phase 2 Verification Checkpoint**: Update `CustomTokenService` in [`ServiceCollectionExtensionsOverrideTests.cs`](../../tests/Sitecore.API.Foundation.Tests/ServiceCollectionExtensionsOverrideTests.cs) to implement the 2-parameter `ISitecoreTokenService` methods and run `dotnet build --configuration Release --no-restore` with 0 errors and 0 warnings.

---

### Phase 3: Unit & Integration Test Suite Updates

#### 17. [`tests/Sitecore.API.Foundation.Tests/ServiceCollectionExtensionsOverrideTests.cs`](../../tests/Sitecore.API.Foundation.Tests/ServiceCollectionExtensionsOverrideTests.cs) (Modify)

*   **Type / Scope**: Unit Tests
*   **Proposed Changes**: Update internal `CustomTokenService` to implement both 1-parameter and 2-parameter (`CancellationToken`) overloads of `ISitecoreTokenService`.
*   **Rationale**: Keeps the custom service override test compiling against the expanded `ISitecoreTokenService` interface.

#### 18. `tests/Sitecore.API.Foundation.Tests/SitecoreAuthExceptionTests.cs` (Create)

*   **Type / Scope**: Unit Tests (Restored from PR #27 `667cbfb`)
*   **Proposed Changes**: Restore verbatim via `git checkout 667cbfb -- tests/Sitecore.API.Foundation.Tests/SitecoreAuthExceptionTests.cs`.
*   **Rationale**: Recovers constructor unit tests for `SitecoreAuthException`, `SitecoreAuthHttpException`, and `SitecoreAuthResponseException` deleted in `f986f15`.

#### 19. `tests/Sitecore.API.Foundation.Tests/Models/SitecoreAuthClientCredentialsTests.cs` (Create)

*   **Type / Scope**: Unit Tests (Restored from PR #22 `559e80a` & Merged with PR #18)
*   **Proposed Changes**: Create `SitecoreAuthClientCredentialsTests` (namespace `Sitecore.API.Foundation.Tests.Models`) testing valid construction, `ArgumentNullException` for null/empty `ClientId` and `ClientSecret` (PR #18), value equality, and `ToString()` masking `ClientSecret = ***` without containing the raw secret (PR #22).
*   **Rationale**: Restores the test file deleted in `f986f15` and incorporates all validation test cases from PR #18.

#### 20. [`tests/Sitecore.API.Foundation.Tests/SitecoreAuthTokenTests.cs`](../../tests/Sitecore.API.Foundation.Tests/SitecoreAuthTokenTests.cs) (Modify)

*   **Type / Scope**: Unit Tests
*   **Proposed Changes**: Add `ToString_ShouldMaskAccessToken` verifying `token.ToString()` equals `$"SitecoreAuthToken {{ AccessToken = ***, Expiration = {expiration} }}"` and does not contain the raw `AccessToken`.
*   **Rationale**: Verifies the `SitecoreAuthToken.ToString()` security fix.

#### 21. `tests/Sitecore.API.Foundation.Tests/ResilienceTests.cs` (Create)

*   **Type / Scope**: Unit Tests (Adapted from `origin/feature/asyn-updates-and-reliability` `af270db`)
*   **Proposed Changes**: Add xUnit tests in namespace `Sitecore.API.Foundation.Tests` verifying: (1) `AddSitecoreAuthentication` with `configureResilience` retries transient `HttpStatusCode.ServiceUnavailable` (`503`) responses and succeeds on subsequent `200 OK`; (2) `AddSitecoreAuthenticationSingleton` with `configureResilience` preserves `ServiceLifetime.Singleton` and retries transient failures; (3) `configureClient` customizes the named `HttpClient` (`Constants.SitecoreHttpClientName`); and (4) `httpClientFactory` overrides `HttpClient` creation.
*   **Rationale**: Validates the HTTP resilience, client builder, and custom factory overloads across both lifetimes.

#### 22. [`tests/Sitecore.API.Foundation.Tests/SitecoreTokenServiceTests.cs`](../../tests/Sitecore.API.Foundation.Tests/SitecoreTokenServiceTests.cs) (Modify)

*   **Type / Scope**: Unit Tests
*   **Proposed Changes**:
    *   Restore `CloneResponseAsync` and async response factory handling in `TestHttpMessageHandler` from PR #25 (`22d3c5f`), and check `if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<HttpResponseMessage>(cancellationToken);` at the start of `SendAsync`.
    *   Add `GetSitecoreAuthToken_WithCancellationToken_ShouldThrowTaskCanceledException` and `TryRefreshSitecoreAuthToken_WithCancellationToken_ShouldThrowTaskCanceledException` asserting `Should.ThrowAsync<TaskCanceledException>()`.
    *   Add unit tests verifying that non-loopback `http://auth.sitecorecloud.io/oauth/token` and credential-bearing `https://user:secret@auth.sitecorecloud.io/oauth/token` throw `SitecoreAuthException`, loopback `http://localhost/oauth/token` and `http://127.0.0.1/oauth/token` succeed, and `expires_in` values `0` and `-1` throw `SitecoreAuthResponseException`.
*   **Rationale**: Restores PR #25, validates cancellation propagation, and tests `AuthTokenUrl` and `ExpiresIn` validation.

#### 23. [`tests/Sitecore.API.Foundation.Tests/SitecoreTokenServiceLoggingTests.cs`](../../tests/Sitecore.API.Foundation.Tests/SitecoreTokenServiceLoggingTests.cs) (Modify)

*   **Type / Scope**: Logging Unit Tests
*   **Proposed Changes**: Attach `Content = new StringContent("sensitive-error-body")` to the stubbed `HttpResponseMessage(HttpStatusCode.BadRequest)` in `GetSitecoreAuthToken_HttpFailure_LogsWarningAndThrows` and assert `!o.ToString()!.Contains("sensitive-error-body")`; assert in JSON parse failure and invalid `UserInfo` URL tests that raw response bodies and embedded credentials are never emitted in log messages.
*   **Rationale**: Non-vacuously guards against regressions of PR #29 and `UserInfo` credential leakage.

#### 24. [`tests/Sitecore.API.Foundation.Authorization.IntegrationTests/Tests/TokenServiceIntegrationTests.cs`](../../tests/Sitecore.API.Foundation.Authorization.IntegrationTests/Tests/TokenServiceIntegrationTests.cs) (Modify)

*   **Type / Scope**: Keycloak Integration Tests
*   **Proposed Changes**: Restore verbatim via `git checkout 5658b10 -- tests/Sitecore.API.Foundation.Authorization.IntegrationTests/Tests/TokenServiceIntegrationTests.cs` (retaining `KeycloakTestFixture.cs` on `Testcontainers` `4.1.0`, which compiles with zero warnings).
*   **Rationale**: Recovers the 150 lines of integration and cache-primed concurrency tests deleted by `f986f15`.

*   **Phase 3 Verification Checkpoint**: Run `dotnet test --configuration Release --filter "FullyQualifiedName!~TokenServiceIntegrationTests&FullyQualifiedName!~GitHubWorkflowTests"` and verify all unit and mock integration tests pass across `net8.0`, `net9.0`, and `net10.0`.

---

### Phase 4: CI/CD Workflows, Documentation, & GitHub Repository Operations

#### 25. [`.actrc`](../../.actrc) (Create — via `c711286`)

*   **Type / Scope**: Local CI Configuration
*   **Proposed Changes**: Cherry-pick `.actrc` from `c711286` (`-P ubuntu-latest=-self-hosted` and `--artifact-server-path /tmp/act-artifacts`).
*   **Rationale**: Enables running `.github/workflows/pr-check.yml` locally via `act`.

#### 26. [`.github/workflows/pr-check.yml`](../../.github/workflows/pr-check.yml) (Modify)

*   **Type / Scope**: GitHub Actions CI Workflow
*   **Proposed Changes**: Include `lint-workflows` from `c711286` using `if command -v actionlint >/dev/null 2>&1; then actionlint -color; else TMP_DIR="$(mktemp -d)" && trap 'rm -rf "$TMP_DIR"' EXIT && bash <(curl -s https://raw.githubusercontent.com/rhysd/actionlint/main/scripts/download-actionlint.bash) latest "$TMP_DIR" && "$TMP_DIR/actionlint" -color; fi`, use `--filter "FullyQualifiedName!~TokenServiceIntegrationTests"`, and remove `-p:TreatWarningsAsErrors=false` from all `dotnet` CLI steps.
*   **Rationale**: Runs unit tests in CI, enforces zero build/NuGet warnings, and avoids leaving an untracked `./actionlint` binary during local `act` runs.

#### 27. [`.github/workflows/nuget-release.yml`](../../.github/workflows/nuget-release.yml) (Modify)

*   **Type / Scope**: GitHub Actions Release Workflow
*   **Proposed Changes**: Update the `dotnet test` filter to `--filter "FullyQualifiedName!~TokenServiceIntegrationTests"` and remove `-p:TreatWarningsAsErrors=false` from all `dotnet` CLI steps.
*   **Rationale**: Executes the full unit test suite and enforces zero warnings prior to packing and publishing NuGet packages.

#### 28. [`tests/Sitecore.API.Foundation.Tests/GitHubWorkflowTests.cs`](../../tests/Sitecore.API.Foundation.Tests/GitHubWorkflowTests.cs) (Create — via `c711286` + Updates)

*   **Type / Scope**: CI Workflow Contract Unit Tests
*   **Proposed Changes**: Include the xUnit contract tests from `c711286`, update `FindRepositoryRoot()` to check for `Sitecore.API.Foundation.Authorization.slnx` instead of `.sln`, and add an assertion verifying neither `pr-check.yml` nor `nuget-release.yml` contains `TreatWarningsAsErrors=false`.
*   **Rationale**: Prevents regressions in GitHub Actions release and PR validation workflows and aligns repository root discovery with `.slnx`.

#### 29. [`README.md`](../../README.md) (Modify)

*   **Type / Scope**: Package & Repository Documentation
*   **Proposed Changes**: Remove `Body: <captured>` and `Raw: <captured>` from the `## Logging` examples (lines 57–59) and update the top badges to reference `pr-check.yml` (`https://img.shields.io/github/actions/workflow/status/acmetz/Sitecore.API.Foundation.Authorization/pr-check.yml?label=CI`) and `.NET 8 | 9 | 10`.
*   **Rationale**: Aligns the NuGet-packaged `README.md` with PR #29's removal of raw OAuth error body logging and the repository's `.NET 8/9/10` target frameworks.

### Repository & GitHub Operations

1.  **GitHub `release` Environment & `NUGET_USER` Secret**:
    *   Create the `release` environment on `acmetz/Sitecore.API.Foundation.Authorization` with `protected_branches=false` and `custom_branch_policies=true`.
    *   Add deployment tag policy `v*` (`type="tag"`) to the `release` environment.
    *   Set `NUGET_USER` to `acmetz` on both the `release` environment (`gh secret set NUGET_USER --env release --body "acmetz"`) and repository (`gh secret set NUGET_USER --body "acmetz"`).
2.  **Pull Request & Post-Merge Branch Cleanup**:
    *   Open the consolidated pull request targeting `main`, and close superseded open pull requests `#17`, `#18`, `#19`, `#20`, `#21`, and `#26` (without `--delete-branch`) with comments linking to the consolidated pull request.
    *   **Post-Merge (once the consolidated PR is merged into `main`)**: Delete the 18 stale/merged remote branches on `origin` (`add-client-credentials-tests-4325591471230124210`, `add-exception-tests-7817374029293058721`, `code-health-di-deduplication-6306318136244721362`, `feat/nuget-trusted-publishing`, `feature/asyn-updates-and-reliability`, `feature/net-10-upgrade`, `fix-sensitive-data-log-5823299237090425502`, `fix-unused-import-sitecoretokenoptions-3331567297844364395`, `fix/code-health-cleanup-logic-10236907127507867444`, `fix/code-health-naming-convention-17826473479557278966`, `fix/remove-unused-constant-15550531955770998918`, `fix/unused-import-isitecoretokencache-15942247074955800931`, `jules-1724118042276693538-d5f874cc`, `optimize-token-cleanup-13625192380360724832`, `performance-optimize-remove-token-676904091108770368`, `performance/optimize-cache-eviction-11676959949113355775`, `remove-unused-system-import-5850024740156727357`, and `security/mask-client-secret-tostring-2375240047124512441`).

### Verification Plan

*   **Dependency Restore & Build**: Run `dotnet restore && dotnet build --configuration Release --no-restore` and verify 0 errors and 0 `NU1901`–`NU1904` warnings across `net8.0`, `net9.0`, and `net10.0`.
*   **Unit & Non-Docker Integration Tests**: Run `dotnet test --configuration Release --no-build --filter "FullyQualifiedName!~TokenServiceIntegrationTests"` and verify all tests pass across `net8.0`, `net9.0`, and `net10.0` (including `GitHubWorkflowTests`, `SitecoreAuthClientCredentialsTests`, `SitecoreAuthExceptionTests`, `ResilienceTests`, and `SitecoreTokenServiceTests`).
*   **Local GitHub Actions Workflow Execution**: Run `act -W .github/workflows/pr-check.yml pull_request` and verify that both `Lint GitHub Workflows` (`actionlint`) and `Build and Test (No Docker)` succeed without leaving untracked files in `git status`.

--------------------------------------------------------------------------------

<!-- Provenance: Indicates initial structural drafting via EGM. -->

*Drafted with EGM*
