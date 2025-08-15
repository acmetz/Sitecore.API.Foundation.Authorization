# Sitecore API Authorization

[![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/acmetz/SitecoreAPIGraphQLClient/ci.yml?branch=main)](https://github.com/acmetz/SitecoreAPIGraphQLClient/actions)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-8%20%7C%209-blue)
[![NuGet](https://img.shields.io/nuget/v/SitecoreAPIAuthorization.svg)](https://www.nuget.org/packages/SitecoreAPIAuthorization/)

A high-performance, thread-safe .NET library for managing Sitecore Cloud authentication tokens with automatic caching, cleanup, and comprehensive integration testing.

## Quick Start

```csharp
// 1. Install the package
dotnet add package Sitecore.API.Foundation.Authorization

// 2. Register services
services.AddLogging();
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
- Logging via Microsoft.Extensions.Logging with structured message templates
- Comprehensive testing framework with unit and integration tests
- Docker integration testing with real Keycloak instances and fast mock server

## Logging

The library now supports structured logging via `ILogger<SitecoreTokenService>`.

- Information: cache hits, token acquisition and caching
- Warning: HTTP failures and invalid refresh attempts
- Error: response parsing failures or missing access_token

Register logging and the library in your DI container:

```csharp
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());
services.AddSitecoreAuthentication(options =>
{
    options.AuthTokenUrl = "https://auth.sitecorecloud.io/oauth/token";
});
```

Example messages:
- Token cache hit for clientId {ClientId}.
- Requesting new token for clientId {ClientId} from {AuthUrl}.
- Token acquired and cached until {Expiration} for clientId {ClientId}.
- Authentication request failed with status {StatusCode} for {AuthUrl}.
- Failed to parse authentication response for clientId {ClientId}.

## Documentation

- [README.md](README.md) - Project overview and usage (this file)
- [CONTRIBUTING.md](CONTRIBUTING.md) - Development and contribution guidelines

## Quick Commands

```bash
# Run all working integration tests
dotnet test Sitecore.API.Foundation.Authorization.IntegrationTests --filter "MockIntegrationTests OR InfrastructureTests"

# Run fast mock integration tests only
dotnet test Sitecore.API.Foundation.Authorization.IntegrationTests --filter "MockIntegrationTests"

# Run all tests (including Docker tests)
dotnet test
```

## Docker Integration Setup

For full Docker integration testing:

```bash
# 1. Switch Docker to Linux containers
# Right-click Docker Desktop "Switch to Linux containers..."

# 2. Pull Keycloak image
docker pull quay.io/keycloak/keycloak:24.0.1

# 3. Run all tests
dotnet test Sitecore.API.Foundation.Authorization.IntegrationTests
```

## License

MIT License - see [LICENSE](LICENSE) file for details.