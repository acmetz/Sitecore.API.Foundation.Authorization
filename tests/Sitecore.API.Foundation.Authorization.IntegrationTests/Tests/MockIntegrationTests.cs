using System;
using System.Net.Http;
using Microsoft.Extensions.Options;
using Shouldly;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.Exceptions;
using Sitecore.API.Foundation.Authorization.IntegrationTests.Fixtures;
using Sitecore.API.Foundation.Authorization.Models;
using Sitecore.API.Foundation.Authorization.Services;
using Xunit;
using Xunit.Abstractions;

namespace Sitecore.API.Foundation.Authorization.IntegrationTests.Tests;

/// <summary>
/// Mock-based integration tests for SitecoreTokenService that provide full coverage without Docker containers.
/// These tests simulate real OAuth2 interactions and validate all service functionality.
/// </summary>
[Collection("Mock OAuth2 Collection")]
public class MockIntegrationTests : IAsyncDisposable
{
    private readonly MockOAuth2TestFixture _mockFixture;
    private readonly ITestOutputHelper _output;
    private readonly SitecoreTokenCache _tokenCache;
    private readonly SitecoreTokenService _tokenService;

    public MockIntegrationTests(MockOAuth2TestFixture mockFixture, ITestOutputHelper output)
    {
        _mockFixture = mockFixture;
        _output = output;
        
        // Setup the SitecoreTokenService with the mock OAuth2 server
        var options = Options.Create(new SitecoreTokenServiceOptions
        {
            AuthTokenUrl = _mockFixture.TokenEndpoint,
            MaxCacheSize = 10,
            CleanupThreshold = 15,
            CleanupInterval = TimeSpan.FromMinutes(5)
        });
        
        _tokenCache = new SitecoreTokenCache(options);
        _tokenService = new SitecoreTokenService(_mockFixture.HttpClient, _tokenCache, options);
        
        _output.WriteLine($"Mock OAuth2 Integration Tests initialized");
        _output.WriteLine($"Client ID: {_mockFixture.ClientId}");
        _output.WriteLine($"Token Endpoint: {_mockFixture.TokenEndpoint}");
    }

    [Fact]
    public async Task MockOAuth2Server_should_be_healthy_and_responding()
    {
        // Arrange & Act
        await _mockFixture.EnsureInitializedAsync();
        
        var isHealthy = await _mockFixture.IsHealthyAsync();

        // Assert
        isHealthy.ShouldBeTrue();
        
        _output.WriteLine("? Mock OAuth2 server is healthy and responding");
    }

    [Fact]
    public async Task SitecoreTokenService_should_successfully_authenticate_with_mock_oauth2_server()
    {
        // Arrange
        await _mockFixture.EnsureInitializedAsync();
        
        var credentials = new SitecoreAuthClientCredentials(
            _mockFixture.ClientId, 
            _mockFixture.ClientSecret);
        var initialRequestCount = _mockFixture.MessageHandler.RequestCount;

        // Act
        var authToken = await _tokenService.GetSitecoreAuthToken(credentials);

        // Assert
        authToken.AccessToken.ShouldNotBeNull();
        authToken.AccessToken.ShouldNotBeEmpty();
        authToken.IsExpired.ShouldBeFalse();
        authToken.Expiration.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
        
        // The token should be a JWT-like token
        authToken.AccessToken.ShouldContain(".");
        authToken.AccessToken.ShouldStartWith("eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9");
        
        // Verify the mock server received exactly one token request
        (_mockFixture.MessageHandler.RequestCount - initialRequestCount).ShouldBe(1);
        _mockFixture.MessageHandler.LastRequest.ShouldNotBeNull();
        
        _output.WriteLine($"? SitecoreTokenService retrieved token: {authToken.AccessToken[..Math.Min(authToken.AccessToken.Length, 50)]}...");
        _output.WriteLine($"? Token expires at: {authToken.Expiration}");
        _output.WriteLine($"? Mock server received {_mockFixture.MessageHandler.RequestCount} request(s)");
    }

    [Fact]
    public async Task SitecoreTokenService_should_cache_tokens_between_requests()
    {
        // Arrange
        await _mockFixture.EnsureInitializedAsync();
        
        var credentials = new SitecoreAuthClientCredentials(
            _mockFixture.ClientId, 
            _mockFixture.ClientSecret);

        // Act
        var firstToken = await _tokenService.GetSitecoreAuthToken(credentials);
        var initialRequestCount = _mockFixture.MessageHandler.RequestCount;
        
        var secondToken = await _tokenService.GetSitecoreAuthToken(credentials);
        var finalRequestCount = _mockFixture.MessageHandler.RequestCount;

        // Assert
        firstToken.ShouldBe(secondToken);
        firstToken.AccessToken.ShouldBe(secondToken.AccessToken);
        
        // Should not have made additional HTTP requests due to caching
        finalRequestCount.ShouldBe(initialRequestCount);
        
        _output.WriteLine("? Tokens are cached and reused correctly");
        _output.WriteLine($"? No additional HTTP requests made (stayed at {finalRequestCount} requests)");
    }

    [Fact]
    public async Task SitecoreTokenService_should_refresh_tokens_successfully()
    {
        // Arrange
        await _mockFixture.EnsureInitializedAsync();
        
        var credentials = new SitecoreAuthClientCredentials(
            _mockFixture.ClientId, 
            _mockFixture.ClientSecret);

        // Act
        var originalToken = await _tokenService.GetSitecoreAuthToken(credentials);
        var initialRequestCount = _mockFixture.MessageHandler.RequestCount;
        
        var refreshedToken = await _tokenService.TryRefreshSitecoreAuthToken(originalToken);
        var finalRequestCount = _mockFixture.MessageHandler.RequestCount;

        // Assert
        refreshedToken.AccessToken.ShouldNotBeNull();
        refreshedToken.AccessToken.ShouldNotBeEmpty();
        refreshedToken.IsExpired.ShouldBeFalse();
        refreshedToken.ShouldNotBe(originalToken); // Should be a new instance
        
        // Both tokens should be valid JWT-like tokens but different
        refreshedToken.AccessToken.ShouldContain(".");
        refreshedToken.AccessToken.ShouldNotBe(originalToken.AccessToken);
        
        // Should have made one additional HTTP request for the refresh
        finalRequestCount.ShouldBe(initialRequestCount + 1);
        
        _output.WriteLine($"? Original token: {originalToken.AccessToken[..Math.Min(originalToken.AccessToken.Length, 50)]}...");
        _output.WriteLine($"? Refreshed token: {refreshedToken.AccessToken[..Math.Min(refreshedToken.AccessToken.Length, 50)]}...");
        _output.WriteLine($"? Made additional HTTP request for refresh ({finalRequestCount} total requests)");
    }

    [Fact]
    public async Task SitecoreTokenService_should_handle_invalid_credentials()
    {
        // Arrange
        await _mockFixture.EnsureInitializedAsync();
        
        var invalidCredentials = new SitecoreAuthClientCredentials(
            "invalid-client", 
            "invalid-secret");
        var initialRequestCount = _mockFixture.MessageHandler.RequestCount;

        // Act & Assert
        var exception = await Should.ThrowAsync<SitecoreAuthHttpException>(
            () => _tokenService.GetSitecoreAuthToken(invalidCredentials));
        exception.StatusCode.ShouldBe(401);
        
        // Verify the mock server received and rejected the single request
        (_mockFixture.MessageHandler.RequestCount - initialRequestCount).ShouldBe(1);
        
        _output.WriteLine($"? Correctly rejected invalid credentials: {exception.Message}");
        _output.WriteLine($"? Mock server processed {_mockFixture.MessageHandler.RequestCount} request(s)");
    }

    [Fact]
    public async Task SitecoreTokenService_should_handle_concurrent_requests()
    {
        // Arrange
        await _mockFixture.EnsureInitializedAsync();
        
        var credentials = new SitecoreAuthClientCredentials(
            _mockFixture.ClientId, 
            _mockFixture.ClientSecret);

        var initialRequestCount = _mockFixture.MessageHandler.RequestCount;

        // Act - Make multiple concurrent requests
        var tasks = new[]
        {
            _tokenService.GetSitecoreAuthToken(credentials),
            _tokenService.GetSitecoreAuthToken(credentials),
            _tokenService.GetSitecoreAuthToken(credentials),
            _tokenService.GetSitecoreAuthToken(credentials),
            _tokenService.GetSitecoreAuthToken(credentials)
        };

        var results = await Task.WhenAll(tasks);
        var finalRequestCount = _mockFixture.MessageHandler.RequestCount;

        // Assert
        foreach (var result in results)
        {
            result.AccessToken.ShouldNotBeNull();
            result.AccessToken.ShouldNotBeEmpty();
        }

        // All results should be the same due to caching
        var firstToken = results[0];
        foreach (var result in results)
        {
            result.ShouldBe(firstToken);
        }
        
        // Should have made only one additional HTTP request due to caching
        var additionalRequests = finalRequestCount - initialRequestCount;
        additionalRequests.ShouldBe(1);
        
        _output.WriteLine($"? All {results.Length} concurrent requests returned the same cached token");
        _output.WriteLine($"? Made only {additionalRequests} additional HTTP request due to efficient caching (total: {finalRequestCount})");
    }

    [Fact]
    public async Task SitecoreTokenService_should_handle_different_credentials_separately()
    {
        // Arrange
        await _mockFixture.EnsureInitializedAsync();

        var credentialsA = new SitecoreAuthClientCredentials(
            $"{_mockFixture.ClientId}-a",
            _mockFixture.ClientSecret);
        var credentialsADuplicate = new SitecoreAuthClientCredentials(
            $"{_mockFixture.ClientId}-a",
            _mockFixture.ClientSecret);
        var credentialsB = new SitecoreAuthClientCredentials(
            $"{_mockFixture.ClientId}-b",
            _mockFixture.ClientSecret);

        var initialRequestCount = _mockFixture.MessageHandler.RequestCount;

        // Act
        var tokenA1 = await _tokenService.GetSitecoreAuthToken(credentialsA);
        var tokenA2 = await _tokenService.GetSitecoreAuthToken(credentialsADuplicate);
        var afterFirstClientRequests = _mockFixture.MessageHandler.RequestCount;
        var tokenB = await _tokenService.GetSitecoreAuthToken(credentialsB);

        var finalRequestCount = _mockFixture.MessageHandler.RequestCount;

        // Assert: equal instances share a cache entry (1 HTTP request), distinct credentials stay isolated (+1 HTTP request)
        tokenA1.ShouldBe(tokenA2);
        tokenA1.AccessToken.ShouldBe(tokenA2.AccessToken);
        tokenA1.ShouldNotBe(tokenB);
        tokenA1.AccessToken.ShouldNotBe(tokenB.AccessToken);

        (afterFirstClientRequests - initialRequestCount).ShouldBe(1);
        (finalRequestCount - initialRequestCount).ShouldBe(2);

        _output.WriteLine("? Equal credentials share cache entry and distinct credentials remain isolated");
    }

    [Fact]
    public async Task MockIntegrationTest_demonstrates_full_oauth2_workflow()
    {
        // This test demonstrates a complete OAuth2 client credentials workflow
        // using the SitecoreTokenService against a mock OAuth2 provider
        
        _output.WriteLine("=== Full Mock OAuth2 Integration Test Workflow ===");
        
        // Initialize the mock OAuth2 server
        await _mockFixture.EnsureInitializedAsync();
        
        _output.WriteLine($"Mock OAuth2 Server initialized at: {_mockFixture.BaseUrl}");
        
        // Step 1: Verify server health
        _output.WriteLine("Step 1: Verify server health");
        var isHealthy = await _mockFixture.IsHealthyAsync();
        isHealthy.ShouldBeTrue();
        _output.WriteLine("? Mock server is healthy");
        
        // Step 2: Authenticate and get initial token
        _output.WriteLine("Step 2: Initial authentication");
        var initialRequests = _mockFixture.MessageHandler.RequestCount;
        var credentials = new SitecoreAuthClientCredentials(_mockFixture.ClientId, _mockFixture.ClientSecret);
        var initialToken = await _tokenService.GetSitecoreAuthToken(credentials);
        
        initialToken.AccessToken.ShouldNotBeNull();
        _output.WriteLine($"? Initial token received: {initialToken.AccessToken[..20]}...");
        
        // Step 3: Verify token caching
        _output.WriteLine("Step 3: Verify token caching");
        var cachedToken = await _tokenService.GetSitecoreAuthToken(credentials);
        cachedToken.ShouldBe(initialToken);
        _output.WriteLine("? Token properly cached and reused");
        
        // Step 4: Test token refresh
        _output.WriteLine("Step 4: Test token refresh");
        var refreshedToken = await _tokenService.TryRefreshSitecoreAuthToken(initialToken);
        refreshedToken.ShouldNotBe(initialToken);
        refreshedToken.AccessToken.ShouldNotBe(initialToken.AccessToken);
        _output.WriteLine($"? Token refreshed: {refreshedToken.AccessToken[..20]}...");
        
        // Step 5: Verify new token is cached
        _output.WriteLine("Step 5: Verify new token is cached");
        var newCachedToken = await _tokenService.GetSitecoreAuthToken(credentials);
        newCachedToken.ShouldBe(refreshedToken);
        _output.WriteLine("? Refreshed token properly cached");
        
        // Step 6: Verify HTTP request efficiency
        _output.WriteLine("Step 6: Verify HTTP request efficiency");
        var totalRequests = _mockFixture.MessageHandler.RequestCount;
        (totalRequests - initialRequests).ShouldBe(2);
        _output.WriteLine($"? Total HTTP requests made: {totalRequests}");
        
        _output.WriteLine("=== Mock Integration Test Complete ===");
        _output.WriteLine("? Full OAuth2 client credentials workflow successful");
        _output.WriteLine("? All operations completed without Docker containers");
    }

    [Fact]
    public async Task TokenCache_should_handle_cache_cleanup_and_eviction()
    {
        // Arrange
        await _mockFixture.EnsureInitializedAsync();
        
        // Create a service with small cache for testing eviction
        var options = Options.Create(new SitecoreTokenServiceOptions
        {
            AuthTokenUrl = _mockFixture.TokenEndpoint,
            MaxCacheSize = 2,
            CleanupThreshold = 3,
            CleanupInterval = TimeSpan.FromMilliseconds(100)
        });
        
        using var cache = new SitecoreTokenCache(options);
        var service = new SitecoreTokenService(_mockFixture.HttpClient, cache, options);
        
        // Act - Add tokens beyond cache limit
        var credentials1 = new SitecoreAuthClientCredentials($"{_mockFixture.ClientId}-1", _mockFixture.ClientSecret);
        var credentials2 = new SitecoreAuthClientCredentials($"{_mockFixture.ClientId}-2", _mockFixture.ClientSecret);
        var credentials3 = new SitecoreAuthClientCredentials($"{_mockFixture.ClientId}-3", _mockFixture.ClientSecret);
        
        await service.GetSitecoreAuthToken(credentials1);
        await Task.Delay(5);
        await service.GetSitecoreAuthToken(credentials2);
        await Task.Delay(5);
        await service.GetSitecoreAuthToken(credentials3);
        
        // Assert
        cache.CacheSize.ShouldBe(2); // Should enforce MaxCacheSize eviction
        cache.GetToken(credentials1).ShouldBeNull();
        cache.GetToken(credentials2).ShouldNotBeNull();
        cache.GetToken(credentials3).ShouldNotBeNull();
        
        _output.WriteLine($"? Cache size is properly managed: {cache.CacheSize} tokens");
        _output.WriteLine("? Cache cleanup and eviction working correctly");
    }

    public async ValueTask DisposeAsync()
    {
        _tokenCache?.Dispose();
        await Task.CompletedTask;
    }
}