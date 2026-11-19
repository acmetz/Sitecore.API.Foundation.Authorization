
# Sitecore.API.Foundation.Authorization

A high-performance, thread-safe .NET library for managing Sitecore Cloud authentication tokens with automatic caching, cleanup, and dependency injection support.

## Project Overview

This library provides a robust, production-ready solution for OAuth2 authentication with Sitecore Cloud services, featuring intelligent token caching, automatic refresh, and comprehensive integration testing capabilities.

### Solution Architecture

```
Sitecore.API.Foundation.Authorization/
    Sitecore.API.Foundation.Authorization.slnx
    docs/                                         # Project and design documentation
        README.md                                 # Technical architecture & integration documentation
        designs/                                  # Technical design specifications (EGM model)
    src/
        README.md                                 # EGM system blueprint & global invariants
        Sitecore.API.Foundation.Authorization/    # Main library (net10.0;net11.0)
            README.md                             # Project module index & dependency graph
            Abstractions/                                 # Interfaces and contracts (+ README.md contract)
                ISitecoreTokenService.cs
                ISitecoreTokenCache.cs
            Configuration/                                # Configuration and constants (+ README.md contract)
                SitecoreTokenServiceOptions.cs
                Constants.cs
            DependencyInjection/                          # Service registration & HTTP resilience extensions (+ README.md contract)
                ISitecoreAuthenticationBuilder.cs
                ServiceCollectionExtensions.cs
            Exceptions/                                   # Custom exception hierarchy (+ README.md contract)
                SitecoreAuthException.cs
            Models/                                       # Data models with secret/token ToString() masking (+ README.md contract)
                SitecoreAuthToken.cs
                SitecoreAuthClientCredentials.cs
            Services/                                     # Service implementations (+ README.md contract)
                SitecoreTokenService.cs
                SitecoreTokenCache.cs
    tests/
        Sitecore.API.Foundation.Tests/                    # Unit & CI workflow contract tests
        Sitecore.API.Foundation.Authorization.IntegrationTests/  # Integration tests
            Fixtures/                                     # Test fixtures and infrastructure
                InMemoryLogger.cs                         # In-memory ILogger capture helper
                KeycloakTestFixture.cs                    # Real Keycloak container management
                MockOAuth2TestFixture.cs                  # Mock OAuth2 server
                MockOAuth2MessageHandler.cs               # HTTP message handler
            Tests/                                        # Test implementations
                InfrastructureTests.cs                    # Basic infrastructure validation
                LoggingIntegrationTests.cs                # Structured logging integration validation
                MockIntegrationTests.cs                   # Mock-based integration tests
                TokenServiceIntegrationTests.cs           # Real Keycloak integration tests
            Collections/                                  # Test collection definitions
            Realm/                                        # Keycloak realm configuration
                test-realm.json
        Sitecore.API.Foundation.Authorization.Benchmarks/ # BenchmarkDotNet cache microbenchmarks
            Program.cs
            TokenCacheBenchmarks.cs
```

## Features

### Core Features
- **Multi-targeted for .NET 10 and 11** (`net10.0;net11.0`)
- **Typed multi-client authentication** (`AddSitecoreAuthentication<TClient>()` and `ISitecoreTokenService<TClient>`) with fluent `ISitecoreAuthenticationBuilder<TClient>` configuration and zero collision with `AddHttpClient<TClient>()`
- **Thread-safe token caching** using `ConcurrentDictionary` partitioned by `(Credentials, AuthTokenUrl)` with atomic `KeyValuePair` removal
- **Automatic token refresh** and expiration handling with `CancellationToken` support
- **Standard HTTP resilience** via `Microsoft.Extensions.Http.Resilience` (`HttpStandardResilienceOptions`)
- **Configurable cache management** with bounded `PriorityQueue` earliest-expiration eviction and periodic cleanup
- **Endpoint & secret hardening**: HTTPS (or loopback HTTP) `AuthTokenUrl` validation, `ToString()` masking (`***`) for `ClientSecret` and `AccessToken`, and zero raw OAuth body logging
- **Dependency injection integration** with `Scoped` and `Singleton` registration patterns
- **Configuration-based setup** with `appsettings.json` support
- **Comprehensive exception handling** with custom exception types

### Testing Features
- **Comprehensive test suite** with integration tests

- **Docker-based integration testing** with real Keycloak instances
- **Mock OAuth2 server** for fast, reliable testing without Docker
- **Smart Docker detection** with automatic fallback to mock mode
- **Clear error guidance** for Docker configuration issues
- **Fast mock tests** completing in under 1 second
- **Full OAuth2 workflow validation** including token refresh and caching

### Integration Test Status


**Mock Integration Tests**: reliable
**Infrastructure Tests**: passing
**Docker Integration Tests**: requires Linux containers

## Installation

### Package Manager Console
```powershell
Install-Package SitecoreAPIAuthorization
```

### .NET CLI
```bash
dotnet add package SitecoreAPIAuthorization
```

### PackageReference
```xml
<PackageReference Include="SitecoreAPIAuthorization" />
```

## Quick Start

### 1. Basic Setup with Dependency Injection

```csharp
using Microsoft.Extensions.DependencyInjection;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.DependencyInjection;
using Sitecore.API.Foundation.Authorization.Models;

// Configure services
services.AddSitecoreAuthentication();

// Use in your service/controller
public class MyService
{
    private readonly ISitecoreTokenService _tokenService;
    
    public MyService(ISitecoreTokenService tokenService)
    {
        _tokenService = tokenService;
    }
    
    public async Task<SitecoreAuthToken> GetTokenAsync()
    {
        var credentials = new SitecoreAuthClientCredentials("your-client-id", "your-client-secret");
        
        return await _tokenService.GetSitecoreAuthToken(credentials);
    }
}
```

### 2. Configuration-based Setup

**appsettings.json:**
```json
{
  "SitecoreAuthentication": {
    "MaxCacheSize": 25,
    "CleanupThreshold": 30,
    "CleanupInterval": "00:10:00",
    "AuthTokenUrl": "https://auth.sitecorecloud.io/oauth/token"
  }
}
```

**Startup.cs / Program.cs:**
```csharp
services.AddSitecoreAuthentication(configuration);
```

### 3. Testing Setup with Custom Auth URL

For testing scenarios, you can override the authentication endpoint:

```csharp
services.AddSitecoreAuthentication(options =>
{
    options.AuthTokenUrl = "https://test-auth.example.com/oauth/token";
    options.MaxCacheSize = 10; // Smaller cache for testing
});
```

## Architecture Overview

The library follows a clean architecture approach with clear separation of concerns, documented through [Elephant-Goldfish Model (EGM) Design Specifications](designs/README.md), the [System Blueprint (`src/README.md`)](../src/README.md), and the [Library Module Index (`src/Sitecore.API.Foundation.Authorization/README.md`)](../src/Sitecore.API.Foundation.Authorization/README.md):

### Core Abstractions ([Contract](../src/Sitecore.API.Foundation.Authorization/Abstractions/README.md))
- **`ISitecoreTokenService`** - Main interface for token operations
- **`ISitecoreTokenService<TClient>`** - Typed interface for consumer-specific token operations
- **`ISitecoreTokenCache`** - Interface for endpoint-partitioned cache management

### Models ([Contract](../src/Sitecore.API.Foundation.Authorization/Models/README.md))
- **`SitecoreAuthToken`** - Immutable token representation with expiration
- **`SitecoreAuthClientCredentials`** - Immutable credentials record

### Services ([Contract](../src/Sitecore.API.Foundation.Authorization/Services/README.md))
- **`SitecoreTokenService` / `SitecoreTokenService<TClient>`** - Default and typed service implementations
- **`SitecoreTokenCache`** - High-performance concurrent cache partitioned by `(Credentials, AuthTokenUrl)`

### Configuration ([Contract](../src/Sitecore.API.Foundation.Authorization/Configuration/README.md))
- **`SitecoreTokenServiceOptions`** - Configuration options pattern
- **`Constants`** - Internal configuration constants

### Dependency Injection ([Contract](../src/Sitecore.API.Foundation.Authorization/DependencyInjection/README.md))
- **`ServiceCollectionExtensions`** - Untyped and typed (`<TClient>`) `AddSitecoreAuthentication` and `AddSitecoreAuthenticationSingleton` registration and HTTP resilience extensions
- **`ISitecoreAuthenticationBuilder<TClient>`** - Fluent builder for configuring per-type options, resilience, and `HttpClient` behavior

### Exceptions ([Contract](../src/Sitecore.API.Foundation.Authorization/Exceptions/README.md))
- **`SitecoreAuthException`** - Base exception type
- **`SitecoreAuthHttpException`** - HTTP-specific errors
- **`SitecoreAuthResponseException`** - Response parsing errors

## Registration Patterns

The library provides multiple registration patterns to suit different application architectures:

### 1. Scoped Service Registration (Default)

```csharp
// Service: Scoped (new instance per request/scope)
// Cache: Singleton (shared across all requests)
services.AddSitecoreAuthentication();
```

**Best for:** Web applications, APIs where you want a fresh service per request but shared cache.

### 2. Singleton Service Registration

```csharp
// Service: Singleton (shared across entire application)
// Cache: Singleton (shared across entire application)
services.AddSitecoreAuthenticationSingleton();
```

**Best for:** Console applications, background services, or when you want maximum performance.

### 3. Configuration-based Registration

```csharp
// With default section name "SitecoreAuthentication"
services.AddSitecoreAuthentication(configuration);

// With custom section name
services.AddSitecoreAuthentication(configuration, "MyCustomSection");

// Singleton with configuration
services.AddSitecoreAuthenticationSingleton(configuration);
services.AddSitecoreAuthenticationSingleton(configuration, "MyCustomSection");
```

### 4. Options-based Registration

```csharp
services.AddSitecoreAuthentication(options =>
{
    options.MaxCacheSize = 50;
    options.CleanupThreshold = 75;
    options.CleanupInterval = TimeSpan.FromMinutes(15);
    options.AuthTokenUrl = "https://custom-auth.example.com/oauth/token"; // Custom endpoint
});

// Or singleton
services.AddSitecoreAuthenticationSingleton(options =>
{
    options.MaxCacheSize = 100;
    options.CleanupThreshold = 150;
    options.CleanupInterval = TimeSpan.FromMinutes(5);
});
```

### 5. HTTP Resilience & Custom `HttpClient` Registration

```csharp
services.AddSitecoreAuthentication(
    configureOptions: options =>
    {
        options.MaxCacheSize = 50;
    },
    configureResilience: resilience =>
    {
        resilience.Retry.MaxRetryAttempts = 3;
        resilience.Retry.Delay = TimeSpan.FromMilliseconds(200);
    },
    configureClient: clientBuilder =>
    {
        clientBuilder.ConfigureHttpClient(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
    });
```

### 6. Typed Multi-Client Registration (`AddSitecoreAuthentication<TClient>()`)

When an application integrates with multiple Sitecore environments or APIs simultaneously (e.g., Staging and Production, or XM Cloud Authoring and Delivery), register `ISitecoreTokenService<TClient>` alongside `services.AddHttpClient<TClient>()`:

```csharp
// Configure global shared cache capacity on the default options (optional)
services.Configure<SitecoreTokenServiceOptions>(options =>
{
    options.MaxCacheSize = 100;
    options.CleanupThreshold = 150;
});

// Register your typed API client and its dedicated Sitecore authentication pipeline
services.AddHttpClient<XmCloudStagingClient>(client =>
{
    client.BaseAddress = new Uri("https://xmcloud-staging.sitecorecloud.io/");
});

services.AddSitecoreAuthentication<XmCloudStagingClient>()
    .ConfigureOptions(options =>
    {
        options.AuthTokenUrl = "https://staging-auth.sitecorecloud.io/oauth/token";
    })
    .ConfigureResilience(resilience =>
    {
        resilience.Retry.MaxRetryAttempts = 3;
    })
    .ConfigureHttpClient(builder =>
    {
        builder.ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(15));
    });

// Inject both HttpClient and ISitecoreTokenService<TClient> without DI collisions
public class XmCloudStagingClient
{
    private readonly HttpClient _httpClient;
    private readonly ISitecoreTokenService<XmCloudStagingClient> _tokenService;

    public XmCloudStagingClient(
        HttpClient httpClient,
        ISitecoreTokenService<XmCloudStagingClient> tokenService)
    {
        _httpClient = httpClient;
        _tokenService = tokenService;
    }
}
```

> **Note on Shared Cache Options**: All untyped and typed token services share a single `ISitecoreTokenCache` singleton partitioned by `(ClientId, ClientSecret, AuthTokenUrl)`. Global cache capacity and cleanup settings (`MaxCacheSize`, `CleanupThreshold`, and `CleanupInterval`) are read from the default unnamed `IOptions<SitecoreTokenServiceOptions>` registration (`services.Configure<SitecoreTokenServiceOptions>(...)` or non-generic `AddSitecoreAuthentication(...)`), whereas per-client named options configure `AuthTokenUrl` for that `TClient`.

## Configuration Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxCacheSize` | `int` | `10` | Maximum number of tokens to cache before eviction |
| `CleanupThreshold` | `int` | `15` | Number of tokens that triggers automatic cleanup |
| `CleanupInterval` | `TimeSpan` | `5 minutes` | Interval between automatic cleanups |
| `AuthTokenUrl` | `string` | `"https://auth.sitecorecloud.io/oauth/token"` | The authentication endpoint URL (must be an absolute `https://` URI or loopback `http://` URI without embedded credentials) |

### Configuration Examples

**Code-based:**
```csharp
services.AddSitecoreAuthentication(options =>
{
    options.MaxCacheSize = 25;           // Cache up to 25 tokens
    options.CleanupThreshold = 30;       // Cleanup when exceeding 30 tokens
    options.CleanupInterval = TimeSpan.FromMinutes(10); // Cleanup every 10 minutes
    options.AuthTokenUrl = "https://staging-auth.sitecorecloud.io/oauth/token"; // Staging endpoint
});
```

**appsettings.json:**
```json
{
  "SitecoreAuthentication": {
    "MaxCacheSize": 25,
    "CleanupThreshold": 30,
    "CleanupInterval": "00:10:00",
    "AuthTokenUrl": "https://staging-auth.sitecorecloud.io/oauth/token"
  }
}
```

**Custom section:**
```json
{
  "MyApp": {
    "SitecoreAuth": {
      "MaxCacheSize": 50,
      "CleanupThreshold": 75,
      "CleanupInterval": "00:05:00",
      "AuthTokenUrl": "https://dev-auth.example.com/oauth/token"
    }
  }
}
```

```csharp
services.AddSitecoreAuthentication(configuration, "MyApp:SitecoreAuth");
```

## Environment-Specific Configuration

The configurable `AuthTokenUrl` makes it easy to work with different environments:

### Development Environment
```json
{
  "SitecoreAuthentication": {
    "AuthTokenUrl": "https://dev-auth.sitecorecloud.io/oauth/token",
    "MaxCacheSize": 5,
    "CleanupInterval": "00:01:00"
  }
}
```

### Staging Environment
```json
{
  "SitecoreAuthentication": {
    "AuthTokenUrl": "https://staging-auth.sitecorecloud.io/oauth/token",
    "MaxCacheSize": 15,
    "CleanupInterval": "00:05:00"
  }
}
```

### Production Environment
```json
{
  "SitecoreAuthentication": {
    "AuthTokenUrl": "https://auth.sitecorecloud.io/oauth/token",
    "MaxCacheSize": 100,
    "CleanupInterval": "00:10:00"
  }
}
```

### Testing with Custom Mock Server
```csharp
// In your test setup
services.AddSitecoreAuthentication(options =>
{
    options.AuthTokenUrl = "http://localhost:8080/mock-auth/token";
    options.MaxCacheSize = 3; // Small cache for testing
    options.CleanupInterval = TimeSpan.FromSeconds(30); // Frequent cleanup for testing
});
```

## Usage Examples

### Basic Token Retrieval

```csharp
public class SitecoreService
{
    private readonly ISitecoreTokenService _tokenService;
    
    public SitecoreService(ISitecoreTokenService tokenService)
    {
        _tokenService = tokenService;
    }
    
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var credentials = new SitecoreAuthClientCredentials("your-client-id", "your-client-secret");
        
        var token = await _tokenService.GetSitecoreAuthToken(credentials, cancellationToken);
        return token.AccessToken;
    }
}
```

### Token Refresh

```csharp
public async Task<SitecoreAuthToken> RefreshTokenAsync(SitecoreAuthToken currentToken)
{
    try
    {
        return await _tokenService.TryRefreshSitecoreAuthToken(currentToken);
    }
    catch (ArgumentException ex)
    {
        // Token was not managed by this service
        Console.WriteLine($"Token refresh failed: {ex.Message}");
        throw;
    }
}
```

### Manual Cache Management

```csharp
public class CacheManagementService
{
    private readonly ISitecoreTokenCache _tokenCache;
    
    public CacheManagementService(ISitecoreTokenCache tokenCache)
    {
        _tokenCache = tokenCache;
    }
    
    public void CheckCacheStatus()
    {
        Console.WriteLine($"Current cache size: {_tokenCache.CacheSize}");
    }
    
    public void ForceCleanup()
    {
        _tokenCache.PerformCleanup();
        Console.WriteLine($"Cache size after cleanup: {_tokenCache.CacheSize}");
    }
    
    public void ClearAllTokens()
    {
        _tokenCache.ClearCache();
        Console.WriteLine("All tokens cleared from cache");
    }
}
```

### With Error Handling

```csharp
public async Task<SitecoreAuthToken> GetTokenWithErrorHandlingAsync()
{
    var credentials = new SitecoreAuthClientCredentials("your-client-id", "your-client-secret");
    
    try
    {
        return await _tokenService.GetSitecoreAuthToken(credentials);
    }
    catch (SitecoreAuthHttpException httpEx)
    {
        Console.WriteLine($"HTTP Error {httpEx.StatusCode}: {httpEx.Message}");
        Console.WriteLine($"Request URL: {httpEx.RequestUrl}");
        throw;
    }
    catch (SitecoreAuthResponseException responseEx)
    {
        Console.WriteLine($"Response parsing error: {responseEx.Message}");
        throw;
    }
    catch (SitecoreAuthException authEx)
    {
        Console.WriteLine($"General auth error: {authEx.Message}");
        throw;
    }
}
```

## Advanced Scenarios

### Multiple Client Configurations

```csharp
// Register multiple configurations for different environments
services.AddSitecoreAuthentication(options =>
{
    // Production settings
    options.MaxCacheSize = 100;
    options.CleanupThreshold = 150;
    options.CleanupInterval = TimeSpan.FromMinutes(5);
    options.AuthTokenUrl = "https://auth.sitecorecloud.io/oauth/token";
});

// Use different credentials for different services
public class MultiTenantSitecoreService
{
    private readonly ISitecoreTokenService _tokenService;
    
    public async Task<SitecoreAuthToken> GetTokenForTenantAsync(string tenantId)
    {
        var credentials = GetCredentialsForTenant(tenantId);
        return await _tokenService.GetSitecoreAuthToken(credentials);
    }
    
    private SitecoreAuthClientCredentials GetCredentialsForTenant(string tenantId)
    {
        // Retrieve tenant-specific credentials from configuration
        return tenantId switch
        {
            "tenant1" => new SitecoreAuthClientCredentials("tenant1-client-id", "tenant1-secret"),
            "tenant2" => new SitecoreAuthClientCredentials("tenant2-client-id", "tenant2-secret"),
            _ => throw new ArgumentException($"Unknown tenant: {tenantId}")
        };
    }
}
```

### ASP.NET Core Integration

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Register Sitecore authentication
builder.Services.AddSitecoreAuthentication(builder.Configuration);

// Register your services
builder.Services.AddScoped<ISitecoreApiService, SitecoreApiService>();

var app = builder.Build();

// Your API endpoints
app.MapGet("/api/sitecore/token", async (ISitecoreTokenService tokenService) =>
{
    var credentials = new SitecoreAuthClientCredentials("your-client-id", "your-client-secret");
    
    var token = await tokenService.GetSitecoreAuthToken(credentials);
    return Results.Ok(new { AccessToken = token.AccessToken, ExpiresAt = token.Expiration });
});

app.Run();
```

### Background Service Integration

```csharp
public class SitecoreBackgroundService : BackgroundService
{
    private readonly ISitecoreTokenService _tokenService;
    private readonly ILogger<SitecoreBackgroundService> _logger;
    
    public SitecoreBackgroundService(
        ISitecoreTokenService tokenService,
        ILogger<SitecoreBackgroundService> logger)
    {
        _tokenService = tokenService;
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var credentials = new SitecoreAuthClientCredentials("background-service-client", "background-service-secret");
                
                var token = await _tokenService.GetSitecoreAuthToken(credentials);
                _logger.LogInformation("Token retrieved successfully. Expires at: {ExpiresAt}", token.Expiration);
                
                // Perform your background work here
                await DoWorkWithToken(token);
                
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in background service");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
    
    private async Task DoWorkWithToken(SitecoreAuthToken token)
    {
        // Your implementation here
        await Task.CompletedTask;
    }
}

// Register the background service
services.AddHostedService<SitecoreBackgroundService>();
```

## Testing

The library includes a comprehensive integration testing framework with multiple testing strategies:

### Test Architecture Overview

```
Integration Tests (24 total: 15 non-Docker + 9 Docker Keycloak, 1 skipped)
Infrastructure Tests (4)
    Basic test framework validation
    Environment verification
    File system access tests
    Async test patterns
Mock Integration Tests (9)
    Mock OAuth2 server health checks
    Full SitecoreTokenService authentication workflow
    Token caching and reuse validation
    Token refresh functionality
    Concurrent request handling
    Error handling for invalid credentials
    Different credentials isolation
    Full OAuth2 workflow demonstration
    Cache cleanup and eviction testing
Logging Integration Tests (2)
    Service & cache structured log levels and secret/token non-leakage
    Invalid response JSON error logging
Docker Integration Tests (9 total, 1 explicitly skipped)
    Real Keycloak container management
    OAuth2 discovery and token workflow testing
    Cached token reuse, refresh, and expiration handling
    Concurrent requests and client credentials error handling
    Docker / Colima availability verification
```

### Running Tests

```bash
# Run all non-Docker integration tests (recommended)
dotnet test tests/Sitecore.API.Foundation.Authorization.IntegrationTests --filter "MockIntegrationTests|InfrastructureTests|LoggingIntegrationTests"

# Run only mock OAuth2 integration tests (fastest)
dotnet test tests/Sitecore.API.Foundation.Authorization.IntegrationTests --filter "MockIntegrationTests"

# Run infrastructure tests only
dotnet test tests/Sitecore.API.Foundation.Authorization.IntegrationTests --filter "InfrastructureTests"

# Run all integration tests including Docker Keycloak tests
dotnet test tests/Sitecore.API.Foundation.Authorization.IntegrationTests

# Run all tests in solution
dotnet test
```

### Docker Integration Tests Setup

For full Docker integration testing with real Keycloak:

#### Prerequisites
- Docker Desktop installed and running
- Docker configured for Linux containers

#### Quick Setup
```bash
# 1. Switch Docker to Linux containers
# Right-click Docker Desktop tray icon -> "Switch to Linux containers..."

# 2. Pull Keycloak image
docker pull quay.io/keycloak/keycloak:24.0.1

# 3. Run integration tests
dotnet test tests/Sitecore.API.Foundation.Authorization.IntegrationTests
```

#### Expected Results After Docker Setup
- **15/15 non-Docker integration tests** passing, plus Docker `TokenServiceIntegrationTests` (**8/9 active** passing when Docker is configured, **1 explicitly skipped**)
- **Real Keycloak integration** working
- **Full OAuth2 workflow validation**

### Unit Testing with Custom Auth URL

```csharp
[Fact]
public async Task TestSitecoreAuthentication_WithCustomAuthUrl()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddSitecoreAuthentication(options =>
    {
        options.AuthTokenUrl = "https://test-auth.example.com/oauth/token";
        options.MaxCacheSize = 5;
    });
    
    using var serviceProvider = services.BuildServiceProvider();
    var tokenService = serviceProvider.GetRequiredService<ISitecoreTokenService>();
    
    var credentials = new SitecoreAuthClientCredentials("test-client", "test-secret");
    
    // Act & Assert
    var token = await tokenService.GetSitecoreAuthToken(credentials);
    token.AccessToken.ShouldNotBeNullOrEmpty();
    token.IsExpired.ShouldBeFalse();
}
```

### Integration Testing with Mock Server

```csharp
public class SitecoreAuthIntegrationTests : IClassFixture<TestServerFixture>
{
    private readonly TestServerFixture _fixture;
    
    public SitecoreAuthIntegrationTests(TestServerFixture fixture)
    {
        _fixture = fixture;
    }
    
    [Fact]
    public async Task GetToken_WithMockServer_ShouldReturnValidToken()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSitecoreAuthentication(options =>
        {
            options.AuthTokenUrl = _fixture.MockServerUrl + "/oauth/token";
        });
        
        var serviceProvider = services.BuildServiceProvider();
        var tokenService = serviceProvider.GetRequiredService<ISitecoreTokenService>();
        
        // Act
        var credentials = new SitecoreAuthClientCredentials("test-client", "test-secret");
        var token = await tokenService.GetSitecoreAuthToken(credentials);
        
        // Assert
        Assert.NotNull(token);
        Assert.False(token.IsExpired);
        Assert.NotEmpty(token.AccessToken);
    }
}
```

### Mocking for Unit Tests

```csharp
[Fact]
public async Task TestServiceWithMockedTokenService()
{
    // Arrange
    var mockService = Substitute.For<ISitecoreTokenService>();
    var expectedToken = new SitecoreAuthToken("test-token", DateTimeOffset.UtcNow.AddHours(1));
    
    mockService.GetSitecoreAuthToken(Arg.Any<SitecoreAuthClientCredentials>())
               .Returns(expectedToken);
    
    var service = new MyService(mockService);
    
    // Act
    var result = await service.GetTokenAsync();
    
    // Assert
    result.ShouldBe(expectedToken);
    await mockService.Received(1).GetSitecoreAuthToken(Arg.Any<SitecoreAuthClientCredentials>());
}
```

## Performance Considerations

### Cache Performance

The library uses `ConcurrentDictionary` for optimal concurrent performance:


- **Read operations** are lock-free and highly concurrent
- **Write operations** use minimal locking via direct concurrent dictionary indexing (`_tokens[key] = token`)
- **Cleanup operations** use `System.Threading.Lock` with try-lock patterns to avoid blocking reads

### Memory Management

- Zero-allocation periodic cleanup of expired tokens based on `CleanupInterval`
- Bounded max-heap `PriorityQueue` eviction (`O(N log K)`) removing earliest-expiring tokens when exceeding `MaxCacheSize` using a static `DescendingLongComparer`
- Atomic `KeyValuePair` cache removals (`ConcurrentDictionary.TryRemove(kvp)`) preventing concurrent refresh race conditions
- Configurable cache size limits to prevent memory bloat
- Lightweight `System.Threading.Lock` requiring no native handle disposal (`IDisposable` is preserved on `ISitecoreTokenCache` for backward compatibility and custom cache implementations)

For measured `.NET 10` and `.NET 11` (plus historical `.NET 8` and `.NET 9`) BenchmarkDotNet results, see the [Benchmarks](../README.md#benchmarks) section in the root README.

### Recommended Settings

| Application Type | MaxCacheSize | CleanupThreshold | CleanupInterval | AuthTokenUrl |
|------------------|--------------|------------------|-----------------|--------------|
| **High-traffic API** | 100-500 | 150-750 | 2-5 minutes | Production endpoint |
| **Medium-traffic Web App** | 25-100 | 50-150 | 5-10 minutes | Production endpoint |
| **Background Service** | 10-25 | 15-40 | 10-15 minutes | Production endpoint |
| **Console Application** | 5-10 | 10-15 | 15-30 minutes | Production endpoint |
| **Testing/Development** | 3-10 | 5-15 | 30 seconds - 2 minutes | Test/Mock endpoint |

## Exception Handling

The library provides a comprehensive exception hierarchy:

```csharp
SitecoreAuthException (base)
SitecoreAuthHttpException (HTTP-related errors)
SitecoreAuthResponseException (response parsing errors)
```

### Exception Examples

```csharp
try
{
    var token = await _tokenService.GetSitecoreAuthToken(credentials);
}
catch (SitecoreAuthHttpException ex) when (ex.StatusCode == 401)
{
    // Handle authentication failures
    _logger.LogWarning("Authentication failed: {Message}", ex.Message);
}
catch (SitecoreAuthHttpException ex) when (ex.StatusCode >= 500)
{
    // Handle server errors with retry logic
    _logger.LogError(ex, "Server error occurred: {StatusCode}", ex.StatusCode);
}
catch (SitecoreAuthResponseException ex)
{
    // Handle response parsing errors
    _logger.LogError(ex, "Failed to parse authentication response");
}
```

## Thread Safety

The library is fully thread-safe:

- **Token Service**: Thread-safe operations for concurrent token requests
- **Token Cache**: Uses `ConcurrentDictionary` with atomic `KeyValuePair` removals for optimal concurrent access
- **Cleanup Operations**: Non-blocking opportunistic cleanup that doesn't interfere with reads
- **Configuration**: Immutable after initialization

## Integration Test Documentation

For details on running and troubleshooting integration tests, see the [Integration Test Documentation](../tests/Sitecore.API.Foundation.Authorization.IntegrationTests/README.md).

## Troubleshooting

### Common Issues

#### 1. "Unable to resolve service for type 'ISitecoreTokenService'"

**Cause:** Service not registered in DI container.

**Solution:**
```csharp
services.AddSitecoreAuthentication(); // Add this line
```

#### 2. "The provided token is not managed by this service."

**Cause:** Trying to refresh a token that wasn't created by the current service instance, was already evicted or cleared from the cache, or belongs to a different configured `AuthTokenUrl` (for example, calling `TryRefreshSitecoreAuthToken` on an `ISitecoreTokenService<TClient>` bound to a different endpoint).

**Solution:** Only refresh tokens that were obtained from the same service instance (and endpoint) while still present in the cache.

#### 3. HTTP errors with custom auth URL

**Cause:** Invalid or unreachable custom authentication endpoint.

**Solution:**
```csharp
// Verify the URL is correct and accessible
services.AddSitecoreAuthentication(options =>
{
    options.AuthTokenUrl = "https://correct-auth-endpoint.com/oauth/token";
});

// For development/testing, ensure your mock server is running
options.AuthTokenUrl = "http://localhost:8080/mock-auth/token";
```

#### 4. Docker integration test failures

**Cause:** Docker Desktop configured for Windows containers instead of Linux containers.

**Solution:**
```bash
# 1. Right-click Docker Desktop system tray icon
# 2. Select "Switch to Linux containers..."
# 3. Wait for Docker to restart
# 4. Verify: docker info | findstr "OSType" should show "linux"
# 5. Re-run tests: dotnet test tests/Sitecore.API.Foundation.Authorization.IntegrationTests
```

#### 5. High memory usage

**Cause:** Cache size settings too high or cleanup not working properly.

**Solution:**
```csharp
services.AddSitecoreAuthentication(options =>
{
    options.MaxCacheSize = 25;          // Reduce cache size
    options.CleanupThreshold = 30;      // Lower threshold
    options.CleanupInterval = TimeSpan.FromMinutes(2); // More frequent cleanup
});
```

#### 6. Performance issues

**Cause:** Frequent cache evictions or cleanup operations.

**Solution:**
- Increase `MaxCacheSize` if you have sufficient memory
- Adjust `CleanupThreshold` to balance performance vs memory
- Consider using singleton registration for better performance

### Debugging

Enable detailed logging:

```csharp
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Debug);
});
```

Monitor cache statistics:

```csharp
public class CacheMonitoringService
{
    private readonly ISitecoreTokenCache _cache;
    private readonly ILogger<CacheMonitoringService> _logger;
    
    public void LogCacheStats()
    {
        _logger.LogInformation("Cache size: {CacheSize}", _cache.CacheSize);
    }
}
```

## Contributing

Contributions are welcome! Please ensure:

1. All new features have comprehensive tests
2. Code follows the existing patterns and conventions
3. Documentation is updated for new features
4. Performance impact is considered and tested

## License

This project is licensed under the MIT License - see the [LICENSE](../LICENSE) file for details.

## Support

For issues and questions:

1. Check the [troubleshooting section](#troubleshooting)
2. Search existing GitHub issues
3. Create a new issue with detailed reproduction steps
