# Sitecore API Authorization

[![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/your-org/your-repo/ci.yml?branch=main)](https://github.com/your-org/your-repo/actions)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-8%20%7C%209-blue)
[![NuGet](https://img.shields.io/nuget/v/Sitecore.API.Foundation.Authorization.svg)](https://www.nuget.org/packages/Sitecore.API.Foundation.Authorization)

A high-performance, thread-safe .NET library for managing Sitecore Cloud authentication tokens with automatic caching, cleanup, and comprehensive integration testing.

## Quick Start

```csharp
// 1. Install the package
dotnet add package Sitecore.API.Foundation.Authorization

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

- Thread-safe token caching with high-performance concurrent operations
- Automatic token refresh and expiration handling
- Configurable cache management with smart cleanup strategies
- Environment-specific configuration for development, staging, and production
- Dependency injection integration with multiple registration patterns
- Logging via Microsoft.Extensions.Logging with detailed debug and information messages (service and cache)
- Comprehensive testing framework with unit and integration tests
- Docker integration testing with real Keycloak instances and fast mock server

## Logging

The library emits detailed logs that help diagnose authentication and caching behavior.

Service logs:
- Token cache hit for clientId {ClientId}.
- Requesting new token for clientId {ClientId} from {AuthUrl}.
- Auth request payload and response status.
- Authentication request failed with status {StatusCode} for {AuthUrl}. Body: <captured>
- Failed to parse authentication response for clientId {ClientId}. Raw: <captured>
- Authentication response was empty or missing access_token for clientId {ClientId}. Raw: <captured>
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
dotnet test Sitecore.API.Foundation.Authorization.IntegrationTests --filter "MockIntegrationTests OR InfrastructureTests"

# Run logging integration tests only
dotnet test Sitecore.API.Foundation.Authorization.IntegrationTests --filter "FullyQualifiedName~LoggingIntegrationTests"

# Run all tests
dotnet test
```

## License

MIT License - see [LICENSE](LICENSE) file for details.