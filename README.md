# Sitecore API Authorization

[![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/acmetz/Sitecore.API.Foundation.Authorization/pr-check.yml?label=CI)](https://github.com/acmetz/Sitecore.API.Foundation.Authorization/actions)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-blue)
[![NuGet](https://img.shields.io/nuget/v/SitecoreAPIAuthorization.svg)](https://www.nuget.org/packages/SitecoreAPIAuthorization)

A high-performance, thread-safe .NET library for managing Sitecore Cloud authentication tokens with automatic caching, cleanup, and comprehensive integration testing.

## Quick Start

```csharp
// 1. Install the package
dotnet add package SitecoreAPIAuthorization

// 2. Register services
services.AddLogging(b => b.SetMinimumLevel(LogLevel.Debug).AddConsole());
services.AddSitecoreAuthentication();

// 3. Use in your code
public class MyService
{
    private readonly ISitecoreTokenService _tokenService;
    
    public MyService(ISitecoreTokenService tokenService)
    {
        _tokenService = tokenService;
    }
    
    public async Task<SitecoreAuthToken> GetTokenAsync()
    {
        var credentials = new SitecoreAuthClientCredentials("client-id", "client-secret");
        return await _tokenService.GetSitecoreAuthToken(credentials);
    }
}
```

## Key Features

- Multi-targeted for `.NET 8`, `.NET 9`, and `.NET 10`
- Thread-safe token caching (`ConcurrentDictionary` + bounded `PriorityQueue` eviction + atomic `KeyValuePair` removal)
- Automatic token refresh, expiration handling, and `CancellationToken` support
- Built-in HTTP resilience configuration via `Microsoft.Extensions.Http.Resilience`
- Secret and bearer token masking in `ToString()` with zero raw OAuth body logging
- Dependency injection integration (`Scoped` and `Singleton` lifetimes)
- Logging via `Microsoft.Extensions.Logging` with structured debug and information messages
- Comprehensive unit, mock OAuth2, and Docker Keycloak integration tests

## Benchmarks

Measured with [BenchmarkDotNet](https://benchmarkdotnet.org/) against `SitecoreTokenCache` populated with 64 active entries (`MaxCacheSize = 64` for steady-state eviction):

```text
BenchmarkDotNet v0.15.8, macOS 27.0.1 (26A434) [Darwin 27.0.0]
Apple M1 Max, 1 CPU, 10 logical and 10 physical cores
.NET SDK 10.0.401
  .NET 8.0  : .NET 8.0.25 (8.0.2526.11203), Arm64 RyuJIT armv8.0-a
  .NET 9.0  : .NET 9.0.14 (9.0.1426.11910), Arm64 RyuJIT armv8.0-a
  .NET 10.0 : .NET 10.0.12 (10.0.1226.42308), Arm64 RyuJIT armv8.0-a
```

| Method | Runtime | Mean | Error | StdDev | Gen0 | Allocated |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: |
| `GetToken` (Cache Hit) | .NET 8.0 | 35.99 ns | 0.155 ns | 0.129 ns | - | 0 B |
| `GetToken` (Cache Hit) | .NET 9.0 | 33.16 ns | 0.153 ns | 0.135 ns | - | 0 B |
| `GetToken` (Cache Hit) | .NET 10.0 | 34.98 ns | 0.146 ns | 0.130 ns | - | 0 B |
| `SetToken` (Update Existing) | .NET 8.0 | 298.02 ns | 1.432 ns | 1.196 ns | 0.0277 | 176 B |
| `SetToken` (Update Existing) | .NET 9.0 | 301.79 ns | 2.100 ns | 1.754 ns | 0.0277 | 176 B |
| `SetToken` (Update Existing) | .NET 10.0 | 296.61 ns | 1.524 ns | 1.425 ns | 0.0176 | 112 B |
| `RemoveToken` (Set + Atomic Remove) | .NET 8.0 | 2,200.36 ns | 10.811 ns | 9.583 ns | 0.0420 | 264 B |
| `RemoveToken` (Set + Atomic Remove) | .NET 9.0 | 3,109.51 ns | 14.551 ns | 12.899 ns | 0.0420 | 264 B |
| `RemoveToken` (Set + Atomic Remove) | .NET 10.0 | 492.74 ns | 2.210 ns | 2.067 ns | 0.0172 | 112 B |
| `PerformCleanup` (64 Entries) | .NET 8.0 | 3,525.64 ns | 19.202 ns | 17.961 ns | 0.0114 | 88 B |
| `PerformCleanup` (64 Entries) | .NET 9.0 | 3,518.24 ns | 9.872 ns | 9.234 ns | 0.0114 | 88 B |
| `PerformCleanup` (64 Entries) | .NET 10.0 | 1,322.99 ns | 13.985 ns | 16.649 ns | - | 0 B |
| `SetToken` (`MaxCacheSize` Eviction) | .NET 8.0 | 4,983.31 ns | 25.776 ns | 22.850 ns | 0.0610 | 408 B |
| `SetToken` (`MaxCacheSize` Eviction) | .NET 9.0 | 4,853.63 ns | 22.772 ns | 20.187 ns | 0.0610 | 408 B |
| `SetToken` (`MaxCacheSize` Eviction) | .NET 10.0 | 2,091.84 ns | 14.582 ns | 13.640 ns | 0.0381 | 256 B |

Run the benchmark suite locally:

```bash
dotnet run --configuration Release --framework net10.0 --project tests/Sitecore.API.Foundation.Authorization.Benchmarks
```

## Logging

The library emits detailed logs that help diagnose authentication and caching behavior.

Service logs:
- Token cache hit for clientId {ClientId}.
- Requesting new token for clientId {ClientId} from {AuthUrl}.
- Auth request payload and response status.
- Authentication request failed with status {StatusCode} for {AuthUrl}.
- Failed to parse authentication response for clientId {ClientId}.
- Authentication response was empty or missing access_token for clientId {ClientId}.
- Token acquired and cached until {Expiration} for clientId {ClientId}.

Cache logs:
- Cache miss for clientId {ClientId}.
- Cache hit for clientId {ClientId}.
- Token cached for clientId {ClientId} until {Expiration}.
- Evicted {Count} token(s) due to cache size limit.
- Cleanup removed {Count} expired token(s).
- Cache cleared, removed {Count} token(s).
- Removed token for clientId {ClientId} from cache.

Enable console logging at Debug level in development to see these details.

## Quick Commands

```bash
# Run mock and infrastructure integration tests
dotnet test tests/Sitecore.API.Foundation.Authorization.IntegrationTests --filter "MockIntegrationTests|InfrastructureTests"

# Run logging integration tests only
dotnet test tests/Sitecore.API.Foundation.Authorization.IntegrationTests --filter "FullyQualifiedName~LoggingIntegrationTests"

# Run all unit and non-Docker integration tests
dotnet test --filter "FullyQualifiedName!~TokenServiceIntegrationTests"
```

## Documentation

For advanced configuration, `CancellationToken` usage, `Microsoft.Extensions.Http.Resilience` policies, multi-tenant patterns, and Docker Keycloak test setup, see the [Extended Developer & Configuration Guide](https://github.com/acmetz/Sitecore.API.Foundation.Authorization/blob/main/docs/README.md).

## License

MIT License - see [LICENSE](LICENSE) file for details.