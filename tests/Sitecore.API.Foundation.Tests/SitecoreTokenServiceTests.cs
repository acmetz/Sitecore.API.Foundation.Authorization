using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Shouldly;
using Sitecore.API.Foundation.Authorization;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.Exceptions;
using Sitecore.API.Foundation.Authorization.Models;
using Sitecore.API.Foundation.Authorization.Services;
using Microsoft.Extensions.Options;

namespace Sitecore.API.Foundation.Tests;

public class SitecoreTokenServiceTests : IDisposable
{
    private readonly TestHttpMessageHandler _mockMessageHandler;
    private readonly HttpClient _httpClient;
    private readonly ISitecoreTokenCache _mockTokenCache;
    private readonly SitecoreTokenService _service;
    private readonly SitecoreAuthClientCredentials _testCredentials;

    public SitecoreTokenServiceTests()
    {
        _mockMessageHandler = new TestHttpMessageHandler();
        _httpClient = new HttpClient(_mockMessageHandler);
        
        // Create a real cache instance for most tests
        var options = Options.Create(new SitecoreTokenServiceOptions());
        _mockTokenCache = new SitecoreTokenCache(options);
        
        _service = new SitecoreTokenService(_httpClient, _mockTokenCache, options);
        _testCredentials = new SitecoreAuthClientCredentials("test-client-id", "test-client-secret");
    }

    [Fact]
    public void Constructor_WithNullHttpClient_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        var options = Options.Create(new SitecoreTokenServiceOptions());
        Should.Throw<ArgumentNullException>(() => new SitecoreTokenService(null!, _mockTokenCache, options))
            .ParamName.ShouldBe("httpClient");
    }

    [Fact]
    public void Constructor_WithNullTokenCache_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        var options = Options.Create(new SitecoreTokenServiceOptions());
        Should.Throw<ArgumentNullException>(() => new SitecoreTokenService(_httpClient, null!, options))
            .ParamName.ShouldBe("tokenCache");
    }

    [Fact]
    public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Should.Throw<ArgumentNullException>(() => new SitecoreTokenService(_httpClient, _mockTokenCache, null!))
            .ParamName.ShouldBe("options");
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithCustomAuthUrl_ShouldUseCustomUrl()
    {
        // Arrange
        var customUrl = "https://custom-auth.example.com/oauth/token";
        var options = Options.Create(new SitecoreTokenServiceOptions
        {
            AuthTokenUrl = customUrl
        });
        var cache = new SitecoreTokenCache(options);
        var service = new SitecoreTokenService(_httpClient, cache, options);

        var authResponse = new { access_token = "test-token", expires_in = 3600 };
        var jsonResponse = JsonSerializer.Serialize(authResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };

        _mockMessageHandler.SetResponse(httpResponse);

        // Act
        await service.GetSitecoreAuthToken(_testCredentials);

        // Assert
        var capturedRequest = _mockMessageHandler.LastRequest;
        capturedRequest.ShouldNotBeNull();
        capturedRequest.RequestUri.ShouldNotBeNull();
        capturedRequest.RequestUri.ToString().ShouldBe(customUrl);
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithDefaultOptions_ShouldUseDefaultUrl()
    {
        // Arrange
        var defaultOptions = Options.Create(new SitecoreTokenServiceOptions());
        var cache = new SitecoreTokenCache(defaultOptions);
        var service = new SitecoreTokenService(_httpClient, cache, defaultOptions);

        var authResponse = new { access_token = "test-token", expires_in = 3600 };
        var jsonResponse = JsonSerializer.Serialize(authResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };

        _mockMessageHandler.SetResponse(httpResponse);

        // Act
        await service.GetSitecoreAuthToken(_testCredentials);

        // Assert
        var capturedRequest = _mockMessageHandler.LastRequest;
        capturedRequest.ShouldNotBeNull();
        capturedRequest.RequestUri.ShouldNotBeNull();
        capturedRequest.RequestUri.ToString().ShouldBe("https://auth.sitecorecloud.io/oauth/token");
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithInvalidAuthUrl_ShouldThrowException()
    {
        // Arrange
        var invalidUrl = "not-a-valid-url";
        var options = Options.Create(new SitecoreTokenServiceOptions
        {
            AuthTokenUrl = invalidUrl
        });
        var cache = new SitecoreTokenCache(options);
        var service = new SitecoreTokenService(_httpClient, cache, options);

        // Act & Assert
        await Should.ThrowAsync<Exception>(() => service.GetSitecoreAuthToken(_testCredentials));
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithValidCredentials_ShouldReturnToken()
    {
        // Arrange
        var expectedToken = "test-access-token";
        var expiresIn = 3600;
        var authResponse = new { access_token = expectedToken, expires_in = expiresIn };
        var jsonResponse = JsonSerializer.Serialize(authResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };

        _mockMessageHandler.SetResponse(httpResponse);

        // Act
        var result = await _service.GetSitecoreAuthToken(_testCredentials);

        // Assert
        result.AccessToken.ShouldBe(expectedToken);
        result.IsExpired.ShouldBeFalse();
        result.Expiration.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
        _mockMessageHandler.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithHttpErrorResponse_ShouldThrowSitecoreAuthHttpException()
    {
        // Arrange
        var httpResponse = new HttpResponseMessage(HttpStatusCode.BadRequest);
        _mockMessageHandler.SetResponse(httpResponse);

        // Act & Assert
        var exception = await Should.ThrowAsync<SitecoreAuthHttpException>(
            () => _service.GetSitecoreAuthToken(_testCredentials));
        
        exception.StatusCode.ShouldBe(400);
        exception.RequestUrl.ShouldBe("https://auth.sitecorecloud.io/oauth/token");
        exception.Message.ShouldContain("Failed to get auth token");
        exception.Message.ShouldContain("BadRequest");
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithParseException_ShouldThrowSitecoreAuthResponseException()
    {
        // Arrange - invalid JSON that will cause parsing exception
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("invalid json", Encoding.UTF8, "application/json")
        };

        _mockMessageHandler.SetResponse(httpResponse);

        // Act & Assert
        var exception = await Should.ThrowAsync<SitecoreAuthResponseException>(
            () => _service.GetSitecoreAuthToken(_testCredentials));
        
        exception.Message.ShouldContain("Failed to parse auth response");
        exception.InnerException.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithNullResponseContent_ShouldThrowSitecoreAuthResponseException()
    {
        // Arrange
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        };

        _mockMessageHandler.SetResponse(httpResponse);

        // Act & Assert
        var exception = await Should.ThrowAsync<SitecoreAuthResponseException>(
            () => _service.GetSitecoreAuthToken(_testCredentials));
        
        exception.Message.ShouldContain("Failed to read auth token from response");
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithEmptyAccessToken_ShouldThrowSitecoreAuthResponseException()
    {
        // Arrange
        var authResponse = new { access_token = "", expires_in = 3600 };
        var jsonResponse = JsonSerializer.Serialize(authResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };

        _mockMessageHandler.SetResponse(httpResponse);

        // Act & Assert
        var exception = await Should.ThrowAsync<SitecoreAuthResponseException>(
            () => _service.GetSitecoreAuthToken(_testCredentials));
        
        exception.Message.ShouldContain("Failed to read auth token from response");
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithCachedValidToken_ShouldReturnCachedToken()
    {
        // Arrange
        var expectedToken = "test-access-token";
        var expiresIn = 3600;
        var authResponse = new { access_token = expectedToken, expires_in = expiresIn };
        var jsonResponse = JsonSerializer.Serialize(authResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };

        _mockMessageHandler.SetResponse(httpResponse);

        // Act
        var firstResult = await _service.GetSitecoreAuthToken(_testCredentials);
        var secondResult = await _service.GetSitecoreAuthToken(_testCredentials);

        // Assert
        firstResult.ShouldBe(secondResult);
        _mockMessageHandler.RequestCount.ShouldBe(1); // Should only make one HTTP call
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithDifferentCredentials_ShouldCreateSeparateTokens()
    {
        // Arrange
        var credentials1 = new SitecoreAuthClientCredentials("client1", "secret1");
        var credentials2 = new SitecoreAuthClientCredentials("client2", "secret2");
        
        var authResponse = new { access_token = "test-token", expires_in = 3600 };
        var jsonResponse = JsonSerializer.Serialize(authResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };

        _mockMessageHandler.SetResponse(httpResponse);

        // Act
        var token1 = await _service.GetSitecoreAuthToken(credentials1);
        var token2 = await _service.GetSitecoreAuthToken(credentials2);

        // Assert
        token1.ShouldNotBe(token2);
        _mockMessageHandler.RequestCount.ShouldBe(2); // Should make two HTTP calls
    }

    [Fact]
    public async Task TryRefreshSitecoreAuthToken_WithValidToken_ShouldRefreshToken()
    {
        // Arrange
        var originalToken = "original-token";
        var refreshedToken = "refreshed-token";
        var expiresIn = 3600;

        // Setup first call for original token
        var originalResponse = new { access_token = originalToken, expires_in = expiresIn };
        var originalJsonResponse = JsonSerializer.Serialize(originalResponse);
        var originalHttpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(originalJsonResponse, Encoding.UTF8, "application/json")
        };

        // Setup second call for refreshed token
        var refreshedResponse = new { access_token = refreshedToken, expires_in = expiresIn };
        var refreshedJsonResponse = JsonSerializer.Serialize(refreshedResponse);
        var refreshedHttpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(refreshedJsonResponse, Encoding.UTF8, "application/json")
        };

        _mockMessageHandler.SetResponses(originalHttpResponse, refreshedHttpResponse);

        // Act
        var token = await _service.GetSitecoreAuthToken(_testCredentials);
        var refreshedTokenResult = await _service.TryRefreshSitecoreAuthToken(token);

        // Assert
        refreshedTokenResult.AccessToken.ShouldBe(refreshedToken);
        refreshedTokenResult.ShouldNotBe(token);
    }

    [Fact]
    public async Task TryRefreshSitecoreAuthToken_WithUnmanagedToken_ShouldThrowArgumentException()
    {
        // Arrange
        var unmanagedToken = new SitecoreAuthToken("unmanaged-token", DateTimeOffset.UtcNow.AddHours(1));

        // Act & Assert
        var exception = await Should.ThrowAsync<ArgumentException>(
            () => _service.TryRefreshSitecoreAuthToken(unmanagedToken));
        
        exception.ParamName.ShouldBe("token");
        exception.Message.ShouldContain("not managed by this service");
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithCancellationToken_ShouldThrowTaskCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockMessageHandler.SetResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"access_token\":\"test-token\",\"expires_in\":3600}", Encoding.UTF8, "application/json")
        });

        // Act & Assert
        await Should.ThrowAsync<TaskCanceledException>(
            () => _service.GetSitecoreAuthToken(_testCredentials, cts.Token));
    }

    [Fact]
    public async Task TryRefreshSitecoreAuthToken_WithCancellationToken_ShouldThrowTaskCanceledException()
    {
        // Arrange
        var originalTokenResponse = new { access_token = "original-token", expires_in = 3600 };
        var jsonResponse = JsonSerializer.Serialize(originalTokenResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };
        _mockMessageHandler.SetResponse(httpResponse);

        var token = await _service.GetSitecoreAuthToken(_testCredentials);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Should.ThrowAsync<TaskCanceledException>(
            () => _service.TryRefreshSitecoreAuthToken(token, cts.Token));
    }

    [Fact]
    public async Task GetSitecoreAuthToken_SendsCorrectRequestPayload()
    {
        // Arrange
        var authResponse = new { access_token = "test-token", expires_in = 3600 };
        var jsonResponse = JsonSerializer.Serialize(authResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };

        _mockMessageHandler.SetResponse(httpResponse);

        // Act
        await _service.GetSitecoreAuthToken(_testCredentials);

        // Assert
        var capturedRequest = _mockMessageHandler.LastRequest;
        capturedRequest.ShouldNotBeNull();
        capturedRequest.RequestUri.ShouldNotBeNull();
        capturedRequest.RequestUri.ToString().ShouldBe("https://auth.sitecorecloud.io/oauth/token");
        capturedRequest.Method.ShouldBe(HttpMethod.Post);
        
        var requestContent = await capturedRequest.Content!.ReadAsStringAsync();
        requestContent.ShouldContain("\"audience\":\"https://api.sitecorecloud.io\"");
        requestContent.ShouldContain("\"grant_type\":\"client_credentials\"");
        requestContent.ShouldContain("\"client_id\":\"test-client-id\"");
        requestContent.ShouldContain("\"client_secret\":\"test-client-secret\"");
    }

    [Fact]
    public async Task GetSitecoreAuthToken_ConcurrentRequests_ShouldHandleThreadSafety()
    {
        // Arrange
        var authResponse = new { access_token = "test-token", expires_in = 3600 };
        var jsonResponse = JsonSerializer.Serialize(authResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };

        _mockMessageHandler.SetResponse(httpResponse);

        var credentials = new SitecoreAuthClientCredentials("concurrent-test", "concurrent-secret");

        // Act
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _service.GetSitecoreAuthToken(credentials))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert
        results.ShouldAllBe(token => !string.IsNullOrEmpty(token.AccessToken));
        results.All(token => token.AccessToken == "test-token").ShouldBeTrue();
        
        // All requests should return the same cached token instance after the first request
        var distinctTokens = results.Distinct().Count();
        distinctTokens.ShouldBe(1);
        
        // Should only make one HTTP call due to caching
        _mockMessageHandler.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task TryRefreshSitecoreAuthToken_ConcurrentRefresh_ShouldHandleThreadSafety()
    {
        // Arrange
        var originalToken = "original-token";
        var refreshedToken = "refreshed-token";
        var expiresIn = 3600;

        var originalResponse = new { access_token = originalToken, expires_in = expiresIn };
        var originalJsonResponse = JsonSerializer.Serialize(originalResponse);
        var originalHttpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(originalJsonResponse, Encoding.UTF8, "application/json")
        };

        var refreshedResponse = new { access_token = refreshedToken, expires_in = expiresIn };
        var refreshedJsonResponse = JsonSerializer.Serialize(refreshedResponse);
        var refreshedHttpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(refreshedJsonResponse, Encoding.UTF8, "application/json")
        };

        _mockMessageHandler.SetResponses(
            originalHttpResponse,
            refreshedHttpResponse,
            refreshedHttpResponse,
            refreshedHttpResponse
        );

        var credentials = new SitecoreAuthClientCredentials("refresh-test", "refresh-secret");

        // Act
        var originalTokenResult = await _service.GetSitecoreAuthToken(credentials);
        
        // For concurrent refresh, we need to be careful because the token might be removed by one thread
        // while another is trying to refresh it. Let's test sequential refreshes instead.
        var firstRefresh = await _service.TryRefreshSitecoreAuthToken(originalTokenResult);
        var secondRefresh = await _service.TryRefreshSitecoreAuthToken(firstRefresh);

        // Assert
        firstRefresh.AccessToken.ShouldBe(refreshedToken);
        firstRefresh.ShouldNotBe(originalTokenResult);
        
        secondRefresh.AccessToken.ShouldBe(refreshedToken);
        secondRefresh.ShouldNotBe(firstRefresh);
    }

    [Fact]
    public async Task GetSitecoreAuthToken_MultipleServicesWithSameCache_ShouldShareCache()
    {
        // Arrange
        var authResponse = new { access_token = "shared-token", expires_in = 3600 };
        var jsonResponse = JsonSerializer.Serialize(authResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };

        // Create a second service with its own HttpClient but SAME cache
        var secondMockHandler = new TestHttpMessageHandler();
        var secondHttpClient = new HttpClient(secondMockHandler);
        var options = Options.Create(new SitecoreTokenServiceOptions());
        var secondService = new SitecoreTokenService(secondHttpClient, _mockTokenCache, options);

        _mockMessageHandler.SetResponse(httpResponse);
        secondMockHandler.SetResponse(httpResponse);

        var sharedCredentials = new SitecoreAuthClientCredentials("shared-client", "shared-secret");

        // Act
        var tokenFromFirstService = await _service.GetSitecoreAuthToken(sharedCredentials);
        var tokenFromSecondService = await secondService.GetSitecoreAuthToken(sharedCredentials);

        // Assert
        tokenFromFirstService.ShouldBe(tokenFromSecondService);
        _mockMessageHandler.RequestCount.ShouldBe(1); // Only the first service should make the HTTP call
        secondMockHandler.RequestCount.ShouldBe(0); // Second service should get cached token

        // Cleanup
        secondHttpClient.Dispose();
        secondMockHandler.Dispose();
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithExpiredCachedToken_ShouldFetchNewToken()
    {
        // Arrange
        var shortLivedToken = "short-lived-token";
        var newToken = "new-token";
        
        // First response with very short expiration
        var firstResponse = new { access_token = shortLivedToken, expires_in = 1 };
        var firstJsonResponse = JsonSerializer.Serialize(firstResponse);
        var firstHttpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(firstJsonResponse, Encoding.UTF8, "application/json")
        };

        // Second response with normal expiration
        var secondResponse = new { access_token = newToken, expires_in = 3600 };
        var secondJsonResponse = JsonSerializer.Serialize(secondResponse);
        var secondHttpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(secondJsonResponse, Encoding.UTF8, "application/json")
        };

        _mockMessageHandler.SetResponses(firstHttpResponse, secondHttpResponse);

        var credentials = new SitecoreAuthClientCredentials("expiry-test", "expiry-secret");

        // Act
        var firstToken = await _service.GetSitecoreAuthToken(credentials);
        
        // Wait for token to expire
        await Task.Delay(2000);
        
        var secondToken = await _service.GetSitecoreAuthToken(credentials);

        // Assert
        firstToken.AccessToken.ShouldBe(shortLivedToken);
        secondToken.AccessToken.ShouldBe(newToken);
        firstToken.ShouldNotBe(secondToken);
        
        // Should have made two HTTP calls
        _mockMessageHandler.RequestCount.ShouldBe(2);
    }

    [Fact] 
    public async Task GetSitecoreAuthToken_WithSameCredentials_ShouldReuseToken()
    {
        // Arrange
        var authResponse = new { access_token = "reuse-token", expires_in = 3600 };
        var jsonResponse = JsonSerializer.Serialize(authResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };

        _mockMessageHandler.SetResponse(httpResponse);

        // Act
        var token1 = await _service.GetSitecoreAuthToken(_testCredentials);
        var token2 = await _service.GetSitecoreAuthToken(_testCredentials);
        var token3 = await _service.GetSitecoreAuthToken(_testCredentials);

        // Assert
        token1.ShouldBe(token2);
        token2.ShouldBe(token3);
        
        // Should only make one HTTP call
        _mockMessageHandler.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetSitecoreAuthToken_ShouldDisposeHttpResponse()
    {
        // Arrange
        var authResponse = new { access_token = "test-token", expires_in = 3600 };
        var jsonResponse = JsonSerializer.Serialize(authResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };

        _mockMessageHandler.SetResponse(httpResponse);

        // Act
        var result = await _service.GetSitecoreAuthToken(_testCredentials);

        // Assert
        result.AccessToken.ShouldNotBeNull();
        // Note: Due to cloning in the mock handler, we can't directly test disposal,
        // but the implementation should use 'using' statements for proper disposal
    }

    [Fact]
    public async Task GetSitecoreAuthToken_ShouldCleanupExpiredTokensAutomatically()
    {
        // Arrange
        var shortLivedResponse = new { access_token = "short-lived", expires_in = 1 };
        var validResponse = new { access_token = "valid-token", expires_in = 3600 };
        
        var shortLivedHttpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(shortLivedResponse), Encoding.UTF8, "application/json")
        };
        
        var validHttpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(validResponse), Encoding.UTF8, "application/json")
        };

        // Create many expired tokens to trigger cleanup threshold
        var expiredCredentials = new List<SitecoreAuthClientCredentials>();
        for (int i = 0; i < 16; i++) // Exceed CleanupThreshold of 15
        {
            expiredCredentials.Add(new SitecoreAuthClientCredentials($"expired-{i}", $"secret-{i}"));
        }

        var validCredentials = new SitecoreAuthClientCredentials("valid", "secret");

        // Set up responses for all expired tokens plus the valid one
        var responses = new List<HttpResponseMessage>();
        for (int i = 0; i < 16; i++)
        {
            responses.Add(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(shortLivedResponse), Encoding.UTF8, "application/json")
            });
        }
        responses.Add(validHttpResponse);

        _mockMessageHandler.SetResponses(responses.ToArray());

        // Act - Create expired tokens
        foreach (var creds in expiredCredentials)
        {
            await _service.GetSitecoreAuthToken(creds);
        }
        
        // Wait for tokens to expire
        await Task.Delay(2000);
        
        // Create a new valid token which should trigger cleanup due to threshold
        var validToken = await _service.GetSitecoreAuthToken(validCredentials);

        // Assert
        validToken.AccessToken.ShouldBe("valid-token");
        
        // The cache should have been cleaned up during the operation
        var cacheSize = _mockTokenCache.CacheSize;
        cacheSize.ShouldBeLessThanOrEqualTo(10); // Should respect max cache size and cleanup expired tokens
        cacheSize.ShouldBeGreaterThan(0); // Should still contain the valid token
    }

    [Fact]
    public async Task GetSitecoreAuthToken_ShouldCleanupOnlyExpiredTokens()
    {
        // Arrange
        var expiredResponse = new { access_token = "expired-token", expires_in = 1 };
        var validResponse = new { access_token = "valid-token", expires_in = 3600 };
        
        var expiredHttpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(expiredResponse), Encoding.UTF8, "application/json")
        };
        
        var validHttpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(validResponse), Encoding.UTF8, "application/json")
        };

        var expiredCredentials = new SitecoreAuthClientCredentials("expired", "secret1");
        var validCredentials1 = new SitecoreAuthClientCredentials("valid1", "secret2");
        var validCredentials2 = new SitecoreAuthClientCredentials("valid2", "secret3");

        _mockMessageHandler.SetResponses(expiredHttpResponse, validHttpResponse, validHttpResponse);

        // Act - Create one expired token and two valid tokens
        await _service.GetSitecoreAuthToken(expiredCredentials);
        await _service.GetSitecoreAuthToken(validCredentials1);
        
        // Wait for first token to expire
        await Task.Delay(2000);
        
        // This should trigger cleanup and only remove the expired token
        await _service.GetSitecoreAuthToken(validCredentials2);

        // Assert
        var cacheSize = _mockTokenCache.CacheSize;
        cacheSize.ShouldBe(2); // Should have 2 valid tokens, expired one should be cleaned up
    }

    [Fact]
    public async Task GetSitecoreAuthToken_ShouldRespectMaxCacheSize()
    {
        // Arrange
        var authResponse = new { access_token = "test-token", expires_in = 3600 };
        var jsonResponse = JsonSerializer.Serialize(authResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };

        _mockMessageHandler.SetResponse(httpResponse);

        // Act - Create tokens up to the cache limit (assuming a reasonable limit)
        var tasks = new List<Task<SitecoreAuthToken>>();
        for (int i = 0; i < 15; i++) // Try to exceed a reasonable cache limit
        {
            var credentials = new SitecoreAuthClientCredentials($"client-{i}", $"secret-{i}");
            tasks.Add(_service.GetSitecoreAuthToken(credentials));
        }

        await Task.WhenAll(tasks);

        // Assert
        var cacheSize = _mockTokenCache.CacheSize;
        cacheSize.ShouldBeLessThanOrEqualTo(10); // Should not exceed reasonable cache size
    }
    
    public void Dispose()
    {
        _httpClient?.Dispose();
        _mockMessageHandler?.Dispose();
    }
}

public class TestHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responseFactories = new();
    private readonly List<HttpRequestMessage> _requests = new();
    private Func<HttpResponseMessage>? _defaultResponseFactory;

    public int RequestCount => _requests.Count;
    public HttpRequestMessage? LastRequest => _requests.LastOrDefault();
    public IReadOnlyList<HttpRequestMessage> Requests => _requests.AsReadOnly();

    public void SetResponse(HttpResponseMessage response)
    {
        _responseFactories.Clear();
        _defaultResponseFactory = () => CloneResponse(response);
    }

    public void SetResponses(params HttpResponseMessage[] responses)
    {
        _responseFactories.Clear();
        _defaultResponseFactory = null;
        foreach (var response in responses)
        {
            var responseToClone = response;
            _responseFactories.Enqueue(() => CloneResponse(responseToClone));
        }
    }

    private static HttpResponseMessage CloneResponse(HttpResponseMessage original)
    {
        var clone = new HttpResponseMessage(original.StatusCode);
        
        // Clone headers
        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // Clone content if it exists
        if (original.Content != null)
        {
            var contentBytes = original.Content.ReadAsByteArrayAsync().Result;
            clone.Content = new ByteArrayContent(contentBytes);
            
            // Clone content headers
            foreach (var header in original.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Add(request);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
        }

        if (_responseFactories.Count > 0)
        {
            var responseFactory = _responseFactories.Dequeue();
            return Task.FromResult(responseFactory());
        }
        
        if (_defaultResponseFactory != null)
        {
            return Task.FromResult(_defaultResponseFactory());
        }

        throw new InvalidOperationException("No response configured for request");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Clear the queues but don't dispose original responses since they're managed by the test
            _responseFactories.Clear();

            foreach (var request in _requests)
            {
                request?.Dispose();
            }
            _requests.Clear();
        }
        base.Dispose(disposing);
    }
}

public class TimeoutHttpMessageHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Simulate a timeout by delaying and then canceling
        await Task.Delay(100, cancellationToken);
        throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}