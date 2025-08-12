# Sitecore API Authorization

A high-performance, thread-safe .NET library for managing Sitecore Cloud authentication tokens with automatic caching, cleanup, and comprehensive integration testing.

## Quick Start

```csharp
// 1. Install the package
dotnet add package Sitecore.API.Foundation.Authorization

// 2. Register services
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

- **Thread-safe token caching** with high-performance concurrent operations
- **Automatic token refresh** and expiration handling
- **Configurable cache management** with smart cleanup strategies
- **Environment-specific configuration** for development, staging, and production
- **Dependency injection integration** with multiple registration patterns
- **Comprehensive testing framework** with 19 integration tests (16/19 passing)
- **Docker integration testing** with real Keycloak instances
- **Mock OAuth2 server** for fast, reliable testing

## Documentation


For comprehensive documentation including installation, configuration, advanced usage scenarios, testing framework documentation, troubleshooting guide, and performance optimization tips, see:


- [README.md](README.md) - Project overview and usage (this file)
- [CONTRIBUTING.md](CONTRIBUTING.md) - Development and contribution guidelines
- [Integration Test Documentation](docs/README.md) - Detailed integration test setup and troubleshooting
- Source code documentation via XML comments

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