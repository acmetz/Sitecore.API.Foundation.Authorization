using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.Exceptions;
using Sitecore.API.Foundation.Authorization.Models;
using Sitecore.API.Foundation.Authorization.Services;

namespace Sitecore.API.Foundation.Tests;

public class SitecoreTokenServiceTests : IDisposable
{
    private readonly TestHttpMessageHandler _mockMessageHandler;
    private readonly HttpClient _httpClient;
    private readonly ISitecoreTokenCache _tokenCache;
    private readonly SitecoreTokenService _service;
    private readonly SitecoreAuthClientCredentials _testCredentials;

    public SitecoreTokenServiceTests()
    {
        _mockMessageHandler = new TestHttpMessageHandler();
        _httpClient = new HttpClient(_mockMessageHandler);

        // Create a real cache instance for most tests
        var options = Options.Create(new SitecoreTokenServiceOptions());
        _tokenCache = new SitecoreTokenCache(options);

        _service = new SitecoreTokenService(_httpClient, _tokenCache, options);
        _testCredentials = new SitecoreAuthClientCredentials("test-client-id", "test-client-secret");
    }

    [Fact]
    public void Constructor_WithNullHttpClient_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        var options = Options.Create(new SitecoreTokenServiceOptions());
        Should.Throw<ArgumentNullException>(() => new SitecoreTokenService(null!, _tokenCache, options))
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
    public void Constructor_WithNullOptions_OrNullOptionsValue_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Should.Throw<ArgumentNullException>(() => new SitecoreTokenService(_httpClient, _tokenCache, null!))
            .ParamName.ShouldBe("options");

        var nullValueOptions = Substitute.For<IOptions<SitecoreTokenServiceOptions>>();
        nullValueOptions.Value.Returns((SitecoreTokenServiceOptions)null!);
        Should.Throw<ArgumentNullException>(() => new SitecoreTokenService(_httpClient, _tokenCache, nullValueOptions))
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
        using var cache = new SitecoreTokenCache(options);
        var service = new SitecoreTokenService(_httpClient, cache, options);

        var httpResponse = TestHttpResponseHelper.CreateTokenResponse();

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
        using var cache = new SitecoreTokenCache(defaultOptions);
        var service = new SitecoreTokenService(_httpClient, cache, defaultOptions);

        var httpResponse = TestHttpResponseHelper.CreateTokenResponse();

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
        using var cache = new SitecoreTokenCache(options);
        var service = new SitecoreTokenService(_httpClient, cache, options);

        // Act & Assert
        await Should.ThrowAsync<SitecoreAuthException>(() => service.GetSitecoreAuthToken(_testCredentials));
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithValidCredentials_ShouldReturnToken()
    {
        // Arrange
        var expectedToken = "test-access-token";
        var expiresIn = 3600;
        var httpResponse = TestHttpResponseHelper.CreateTokenResponse(expectedToken, expiresIn);

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
        var httpResponse = TestHttpResponseHelper.CreateTokenResponse(accessToken: "", expiresIn: 3600);

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
        var httpResponse = TestHttpResponseHelper.CreateTokenResponse(expectedToken, expiresIn);

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

        var httpResponse = TestHttpResponseHelper.CreateTokenResponse();

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

        var originalHttpResponse = TestHttpResponseHelper.CreateTokenResponse(originalToken, expiresIn);
        var refreshedHttpResponse = TestHttpResponseHelper.CreateTokenResponse(refreshedToken, expiresIn);

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
    public async Task GetSitecoreAuthToken_SendsCorrectRequestPayload()
    {
        // Arrange
        var httpResponse = TestHttpResponseHelper.CreateTokenResponse();

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
        // Arrange - Genuine concurrent execution using an async barrier
        var allArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivedCount = 0;
        const int callerCount = 10;

        using var barrierHandler = new DelegateHttpMessageHandler(async (_, _, ct) =>
        {
            if (Interlocked.Increment(ref arrivedCount) == callerCount)
            {
                allArrived.TrySetResult();
            }

            await allArrived.Task.WaitAsync(ct);
            return TestHttpResponseHelper.CreateTokenResponse("concurrent-token");
        });
        using var concurrentClient = new HttpClient(barrierHandler);
        var options = Options.Create(new SitecoreTokenServiceOptions());
        using var cache = new SitecoreTokenCache(options);
        var concurrentService = new SitecoreTokenService(concurrentClient, cache, options);
        var credentials = new SitecoreAuthClientCredentials("concurrent-test", "concurrent-secret");

        // Act
        var tasks = Enumerable.Range(0, callerCount)
            .Select(_ => concurrentService.GetSitecoreAuthToken(credentials))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert
        results.ShouldAllBe(token => token.AccessToken == "concurrent-token");
        cache.CacheSize.ShouldBe(1);
        cache.GetToken(credentials).ShouldNotBeNull();
    }

    [Fact]
    public async Task TryRefreshSitecoreAuthToken_ConcurrentRefresh_ShouldHandleThreadSafety()
    {
        // Arrange - Prime initial token, then race 5 concurrent refreshes behind a barrier
        _mockMessageHandler.SetResponse(TestHttpResponseHelper.CreateTokenResponse("initial-token"));
        var credentials = new SitecoreAuthClientCredentials("refresh-test", "refresh-secret");
        var initialToken = await _service.GetSitecoreAuthToken(credentials);

        _mockMessageHandler.SetResponse(TestHttpResponseHelper.CreateTokenResponse("refreshed-token"));
        const int contenderCount = 5;
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;

        var tasks = Enumerable.Range(0, contenderCount).Select(_ => Task.Run(async () =>
        {
            if (Interlocked.Increment(ref readyCount) == contenderCount)
            {
                startGate.TrySetResult();
            }

            await startGate.Task;
            return await _service.TryRefreshSitecoreAuthToken(initialToken);
        })).ToArray();

        // Act - Await all individually so we observe exactly 1 success and (contenderCount - 1) ArgumentExceptions
        var successes = new List<SitecoreAuthToken>();
        var failures = new List<ArgumentException>();
        foreach (var task in tasks)
        {
            try
            {
                successes.Add(await task);
            }
            catch (ArgumentException ex)
            {
                failures.Add(ex);
            }
        }

        // Assert
        successes.Count.ShouldBe(1);
        successes[0].AccessToken.ShouldBe("refreshed-token");
        failures.Count.ShouldBe(contenderCount - 1);
        _tokenCache.CacheSize.ShouldBe(1);
        _tokenCache.GetToken(credentials).ShouldBe(successes[0]);
    }

    [Fact]
    public async Task GetSitecoreAuthToken_MultipleServicesWithSameCache_ShouldShareCache()
    {
        // Arrange
        var httpResponse = TestHttpResponseHelper.CreateTokenResponse("shared-token");

        // Create a second service with its own HttpClient but SAME cache
        using var secondMockHandler = new TestHttpMessageHandler();
        using var secondHttpClient = new HttpClient(secondMockHandler);
        var options = Options.Create(new SitecoreTokenServiceOptions());
        var secondService = new SitecoreTokenService(secondHttpClient, _tokenCache, options);

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
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithExpiredCachedToken_ShouldFetchNewToken()
    {
        // Arrange
        var shortLivedToken = "short-lived-token";
        var newToken = "new-token";
        var credentials = new SitecoreAuthClientCredentials("expiry-test", "expiry-secret");
        var expiredToken = new SitecoreAuthToken(shortLivedToken, DateTimeOffset.UtcNow.AddSeconds(-1));

        _tokenCache.SetToken(credentials, expiredToken);
        _mockMessageHandler.SetResponse(TestHttpResponseHelper.CreateTokenResponse(newToken));

        // Act
        var secondToken = await _service.GetSitecoreAuthToken(credentials);

        // Assert
        secondToken.AccessToken.ShouldBe(newToken);
        secondToken.ShouldNotBe(expiredToken);
        _mockMessageHandler.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetSitecoreAuthToken_ShouldDisposeHttpResponseOnSuccessAndError()
    {
        // Arrange
        var successResponse = new TrackingHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"access_token\":\"disposed-tok\",\"expires_in\":3600}", Encoding.UTF8, "application/json")
        };
        var errorResponse = new TrackingHttpResponseMessage(HttpStatusCode.BadRequest);

        var nextResponse = (HttpResponseMessage)successResponse;
        using var handler = new DelegateHttpMessageHandler(_ => nextResponse);
        using var httpClient = new HttpClient(handler);
        var options = Options.Create(new SitecoreTokenServiceOptions());
        using var cache = new SitecoreTokenCache(options);
        var service = new SitecoreTokenService(httpClient, cache, options);

        // Act & Assert - Success path
        var result = await service.GetSitecoreAuthToken(_testCredentials);
        result.AccessToken.ShouldBe("disposed-tok");
        successResponse.IsDisposed.ShouldBeTrue();

        // Act & Assert - Error path
        nextResponse = errorResponse;
        await Should.ThrowAsync<SitecoreAuthHttpException>(() =>
            service.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("other", "secret")));
        errorResponse.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task GetSitecoreAuthToken_ShouldCleanupExpiredTokensAutomatically()
    {
        // Arrange
        var options = Options.Create(new SitecoreTokenServiceOptions
        {
            MaxCacheSize = 20,
            CleanupThreshold = 15,
            CleanupInterval = TimeSpan.FromHours(1)
        });
        using var cache = new SitecoreTokenCache(options);
        var service = new SitecoreTokenService(_httpClient, cache, options);
        cache.PerformCleanup();

        var expiredTimestamp = DateTimeOffset.UtcNow.AddSeconds(-1);
        for (int i = 0; i < 16; i++) // Exceed CleanupThreshold of 15
        {
            cache.SetToken(
                new SitecoreAuthClientCredentials($"expired-{i}", $"secret-{i}"),
                new SitecoreAuthToken("short-lived", expiredTimestamp));
        }

        cache.CacheSize.ShouldBe(16);

        var validCredentials = new SitecoreAuthClientCredentials("valid", "secret");
        _mockMessageHandler.SetResponse(TestHttpResponseHelper.CreateTokenResponse("valid-token"));

        // Act - Request for a new valid token should trigger threshold-based cleanup
        var validToken = await service.GetSitecoreAuthToken(validCredentials);

        // Assert
        validToken.AccessToken.ShouldBe("valid-token");
        cache.CacheSize.ShouldBe(1);
    }

    [Fact]
    public async Task GetSitecoreAuthToken_ShouldCleanupOnlyExpiredTokens()
    {
        // Arrange
        var expiredCredentials = new SitecoreAuthClientCredentials("expired", "secret1");
        var validCredentials1 = new SitecoreAuthClientCredentials("valid1", "secret2");
        var validCredentials2 = new SitecoreAuthClientCredentials("valid2", "secret3");

        _mockMessageHandler.SetResponses(
            TestHttpResponseHelper.CreateTokenResponse("valid-token-1"),
            TestHttpResponseHelper.CreateTokenResponse("valid-token-2"));

        // Act - Cache valid1 (establishes LastCleanup = UtcNow), then seed an expired token, then trigger cleanup on valid2 miss
        await _service.GetSitecoreAuthToken(validCredentials1);
        _tokenCache.SetToken(expiredCredentials, new SitecoreAuthToken("expired-token", DateTimeOffset.UtcNow.AddSeconds(-1)));

        // This should trigger cleanup and only remove the expired token
        await _service.GetSitecoreAuthToken(validCredentials2);

        // Assert
        _tokenCache.CacheSize.ShouldBe(2); // Should have 2 valid tokens, expired one should be cleaned up
        _tokenCache.GetToken(expiredCredentials).ShouldBeNull();
        _tokenCache.GetToken(validCredentials1).ShouldNotBeNull();
        _tokenCache.GetToken(validCredentials2).ShouldNotBeNull();
    }

    [Fact]
    public async Task GetSitecoreAuthToken_ShouldRespectMaxCacheSize()
    {
        // Arrange
        var httpResponse = TestHttpResponseHelper.CreateTokenResponse();

        _mockMessageHandler.SetResponse(httpResponse);

        // Act - Create tokens up to the cache limit
        var tasks = new List<Task<SitecoreAuthToken>>();
        for (int i = 0; i < 15; i++) // Exceed MaxCacheSize of 10
        {
            var credentials = new SitecoreAuthClientCredentials($"client-{i}", $"secret-{i}");
            tasks.Add(_service.GetSitecoreAuthToken(credentials));
        }

        await Task.WhenAll(tasks);

        // Assert
        var cacheSize = _tokenCache.CacheSize;
        cacheSize.ShouldBe(10); // Should enforce MaxCacheSize of 10
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithCancellationToken_ShouldThrowTaskCanceledException()
    {
        // Arrange - Pre-canceled token before any HTTP/cache operation
        using var preCanceledCts = new CancellationTokenSource();
        preCanceledCts.Cancel();

        await Should.ThrowAsync<TaskCanceledException>(
            () => _service.GetSitecoreAuthToken(_testCredentials, preCanceledCts.Token));
        _mockMessageHandler.RequestCount.ShouldBe(0);

        // Arrange - In-flight HTTP cancellation
        using var timeoutHandler = new TimeoutHttpMessageHandler();
        using var timeoutHttpClient = new HttpClient(timeoutHandler);
        var options = Options.Create(new SitecoreTokenServiceOptions());
        using var cache = new SitecoreTokenCache(options);
        var service = new SitecoreTokenService(timeoutHttpClient, cache, options);
        using var inFlightCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(15));

        // Act & Assert
        await Should.ThrowAsync<TaskCanceledException>(
            () => service.GetSitecoreAuthToken(_testCredentials, inFlightCts.Token));
    }

    [Fact]
    public async Task TryRefreshSitecoreAuthToken_WithCancellationToken_ShouldThrowTaskCanceledException()
    {
        // Arrange
        var httpResponse = TestHttpResponseHelper.CreateTokenResponse("initial-token");
        _mockMessageHandler.SetResponse(httpResponse);

        var initialToken = await _service.GetSitecoreAuthToken(_testCredentials);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Should.ThrowAsync<TaskCanceledException>(
            () => _service.TryRefreshSitecoreAuthToken(initialToken, cts.Token));
        _mockMessageHandler.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithNonLoopbackHttpAuthUrl_ShouldThrowSitecoreAuthException()
    {
        // Arrange
        var options = Options.Create(new SitecoreTokenServiceOptions
        {
            AuthTokenUrl = "http://auth.sitecorecloud.io/oauth/token"
        });
        using var cache = new SitecoreTokenCache(options);
        var service = new SitecoreTokenService(_httpClient, cache, options);

        // Act & Assert
        var ex = await Should.ThrowAsync<SitecoreAuthException>(
            () => service.GetSitecoreAuthToken(_testCredentials));
        ex.Message.ShouldContain("HTTPS");
        _mockMessageHandler.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithEmbeddedUserInfoInAuthUrl_ShouldThrowSitecoreAuthException()
    {
        // Arrange
        var options = Options.Create(new SitecoreTokenServiceOptions
        {
            AuthTokenUrl = "https://user:embedded-secret@auth.sitecorecloud.io/oauth/token"
        });
        using var cache = new SitecoreTokenCache(options);
        var service = new SitecoreTokenService(_httpClient, cache, options);

        // Act & Assert
        var ex = await Should.ThrowAsync<SitecoreAuthException>(
            () => service.GetSitecoreAuthToken(_testCredentials));
        ex.Message.ShouldContain("without embedded credentials");
        ex.Message.ShouldNotContain("embedded-secret");
        _mockMessageHandler.RequestCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("http://localhost/oauth/token")]
    [InlineData("http://127.0.0.1/oauth/token")]
    public async Task GetSitecoreAuthToken_WithLoopbackHttpAuthUrl_ShouldSucceed(string loopbackUrl)
    {
        // Arrange
        var options = Options.Create(new SitecoreTokenServiceOptions
        {
            AuthTokenUrl = loopbackUrl
        });
        using var cache = new SitecoreTokenCache(options);
        var service = new SitecoreTokenService(_httpClient, cache, options);

        var httpResponse = TestHttpResponseHelper.CreateTokenResponse("loopback-token");
        _mockMessageHandler.SetResponse(httpResponse);

        // Act
        var token = await service.GetSitecoreAuthToken(_testCredentials);

        // Assert
        token.AccessToken.ShouldBe("loopback-token");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetSitecoreAuthToken_WithNonPositiveExpiresIn_ShouldThrowSitecoreAuthResponseException(int expiresIn)
    {
        // Arrange
        var httpResponse = TestHttpResponseHelper.CreateTokenResponse("test-token", expiresIn);
        _mockMessageHandler.SetResponse(httpResponse);

        // Act & Assert
        var ex = await Should.ThrowAsync<SitecoreAuthResponseException>(
            () => _service.GetSitecoreAuthToken(_testCredentials));
        ex.Message.ShouldContain("non-positive expires_in");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task MisconfiguredEmptyOrNullAuthTokenUrl_WithPopulatedDefaultPartition_ShouldThrowAndNotLeakOrEvictDefaultToken(string? invalidUrl)
    {
        // Arrange - Populate default partition via valid service
        _mockMessageHandler.SetResponse(TestHttpResponseHelper.CreateTokenResponse("prod-secret-token"));
        var prodToken = await _service.GetSitecoreAuthToken(_testCredentials);
        _tokenCache.CacheSize.ShouldBe(1);

        var misconfiguredOptions = Options.Create(new SitecoreTokenServiceOptions
        {
            AuthTokenUrl = invalidUrl!
        });
        var misconfiguredService = new SitecoreTokenService(_httpClient, _tokenCache, misconfiguredOptions);

        // Act & Assert - Neither GetSitecoreAuthToken nor TryRefreshSitecoreAuthToken may hit or evict default partition
        await Should.ThrowAsync<SitecoreAuthException>(() => misconfiguredService.GetSitecoreAuthToken(_testCredentials));
        await Should.ThrowAsync<SitecoreAuthException>(() => misconfiguredService.TryRefreshSitecoreAuthToken(prodToken));

        _tokenCache.GetToken(_testCredentials).ShouldBe(prodToken);
        _mockMessageHandler.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithDefaultStructCredentials_ShouldThrowArgumentException()
    {
        var ex = await Should.ThrowAsync<ArgumentException>(() =>
            _service.GetSitecoreAuthToken(default));
        ex.ParamName.ShouldBe("credentials");
        _mockMessageHandler.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetSitecoreAuthToken_AndTryRefresh_WithDispatchProxyCache_ShouldFallbackToLegacyOneArgMethods()
    {
        // Arrange - DispatchProxy where 2-arg/3-arg methods return default (null) like dynamic mock proxies
        var proxyCache = DispatchProxy.Create<ISitecoreTokenCache, LegacyDispatchProxyCache>();
        var proxyState = (LegacyDispatchProxyCache)(object)proxyCache;

        _mockMessageHandler.SetResponses(
            TestHttpResponseHelper.CreateTokenResponse("dispatch-tok-1"),
            TestHttpResponseHelper.CreateTokenResponse("dispatch-tok-2"));
        var service = new SitecoreTokenService(
            _httpClient,
            proxyCache,
            Options.Create(new SitecoreTokenServiceOptions()));

        // Act - Miss -> Set (3-arg + 2-arg), Hit -> Get (1-arg fallback), Refresh -> Remove (1-arg fallback)
        var t1 = await service.GetSitecoreAuthToken(_testCredentials);
        var t2 = await service.GetSitecoreAuthToken(_testCredentials);
        var refreshed = await service.TryRefreshSitecoreAuthToken(t1);

        // Assert
        t1.AccessToken.ShouldBe("dispatch-tok-1");
        t2.ShouldBe(t1);
        refreshed.AccessToken.ShouldBe("dispatch-tok-2");
        proxyState.OneArgGetCount.ShouldBeGreaterThanOrEqualTo(3);
        proxyState.OneArgSetCount.ShouldBe(2);
        proxyState.OneArgRemoveCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetSitecoreAuthToken_AndTryRefresh_WithNSubstituteOneArgStubs_ShouldFallbackToLegacyMethods()
    {
        var creds = new SitecoreAuthClientCredentials("sub-id", "sub-secret");
        var tok1 = new SitecoreAuthToken("sub-tok-1", DateTimeOffset.UtcNow.AddMinutes(10));
        var mockCache = Substitute.For<ISitecoreTokenCache>();
        mockCache.GetToken(creds).Returns(tok1, (SitecoreAuthToken?)null);
        mockCache.RemoveToken(tok1).Returns(creds);

        _mockMessageHandler.SetResponse(TestHttpResponseHelper.CreateTokenResponse("sub-tok-2"));
        var service = new SitecoreTokenService(
            _httpClient,
            mockCache,
            Options.Create(new SitecoreTokenServiceOptions()));

        var hit = await service.GetSitecoreAuthToken(creds);
        hit.ShouldBe(tok1);

        var refreshed = await service.TryRefreshSitecoreAuthToken(tok1);
        refreshed.AccessToken.ShouldBe("sub-tok-2");

        mockCache.Received(1).RemoveToken(tok1);
        mockCache.Received(1).SetToken(creds, refreshed);
    }

    [Fact]
    public async Task GetSitecoreAuthToken_CanceledDuringBodyRead_AndPreCanceledCacheHit_ShouldPropagateCancellation()
    {
        // 1. Pre-canceled token on cache hit still throws TaskCanceledException before returning cached token
        _mockMessageHandler.SetResponse(TestHttpResponseHelper.CreateTokenResponse("cached-tok"));
        var cached = await _service.GetSitecoreAuthToken(_testCredentials);

        using var preCanceledCts = new CancellationTokenSource();
        preCanceledCts.Cancel();
        await Should.ThrowAsync<TaskCanceledException>(() =>
            _service.GetSitecoreAuthToken(_testCredentials, preCanceledCts.Token));

        // 2. Pre-canceled TryRefreshSitecoreAuthToken preserves initial cached token
        await Should.ThrowAsync<TaskCanceledException>(() =>
            _service.TryRefreshSitecoreAuthToken(cached, preCanceledCts.Token));
        _tokenCache.GetToken(_testCredentials).ShouldBe(cached);

        // 3. In-flight TryRefreshSitecoreAuthToken cancellation during HTTP request
        using var timeoutHandler = new TimeoutHttpMessageHandler();
        using var timeoutClient = new HttpClient(timeoutHandler);
        using var localCache = new SitecoreTokenCache(Options.Create(new SitecoreTokenServiceOptions()));
        localCache.SetToken(_testCredentials, cached);
        var timeoutService = new SitecoreTokenService(timeoutClient, localCache, Options.Create(new SitecoreTokenServiceOptions()));
        using var inFlightRefreshCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(15));
        await Should.ThrowAsync<TaskCanceledException>(() =>
            timeoutService.TryRefreshSitecoreAuthToken(cached, inFlightRefreshCts.Token));

        // 4. Cancellation while reading HTTP response body propagates OperationCanceledException (not SitecoreAuthResponseException)
        using var bodyReadCts = new CancellationTokenSource();
        using var cancelDuringReadHandler = new DelegateHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new CancelingHttpContent(bodyReadCts)
        });
        using var cancelClient = new HttpClient(cancelDuringReadHandler);
        var cancelService = new SitecoreTokenService(
            cancelClient,
            localCache,
            Options.Create(new SitecoreTokenServiceOptions()));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            cancelService.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("body-cancel", "s"), bodyReadCts.Token));
    }

    [Theory]
    [InlineData("http://[::1]/oauth/token")]
    [InlineData("http://[::1]:8080/oauth/token")]
    [InlineData("https://localhost:8443/oauth/token")]
    [InlineData("https://auth.sitecorecloud.io:8443/oauth/token")]
    public async Task GetSitecoreAuthToken_WithAdditionalValidAuthUrls_ShouldSucceed(string validUrl)
    {
        var options = Options.Create(new SitecoreTokenServiceOptions { AuthTokenUrl = validUrl });
        using var cache = new SitecoreTokenCache(options);
        var service = new SitecoreTokenService(_httpClient, cache, options);
        _mockMessageHandler.SetResponse(TestHttpResponseHelper.CreateTokenResponse("valid-url-tok"));

        var token = await service.GetSitecoreAuthToken(_testCredentials);
        token.AccessToken.ShouldBe("valid-url-tok");
    }

    [Theory]
    [InlineData("ftp://auth.sitecorecloud.io/oauth/token")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/oauth/token")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("http://user:pass@localhost/oauth/token")]
    public async Task GetSitecoreAuthToken_WithAdditionalInvalidAuthUrls_ShouldThrowSitecoreAuthException(string? invalidUrl)
    {
        var options = Options.Create(new SitecoreTokenServiceOptions { AuthTokenUrl = invalidUrl! });
        using var cache = new SitecoreTokenCache(options);
        var service = new SitecoreTokenService(_httpClient, cache, options);

        await Should.ThrowAsync<SitecoreAuthException>(() => service.GetSitecoreAuthToken(_testCredentials));
        _mockMessageHandler.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task TryRefreshSitecoreAuthToken_EdgeCases_ShouldHandleExpiredFailureAndEndpointIsolation()
    {
        // 1. Refreshing an expired (not yet evicted/cleaned) cached token fetches a new valid token
        var expired = new SitecoreAuthToken("expired-tok", DateTimeOffset.UtcNow.AddSeconds(-1));
        _tokenCache.SetToken(_testCredentials, expired);
        _mockMessageHandler.SetResponse(TestHttpResponseHelper.CreateTokenResponse("fresh-after-expired"));
        var refreshedFromExpired = await _service.TryRefreshSitecoreAuthToken(expired);
        refreshedFromExpired.AccessToken.ShouldBe("fresh-after-expired");

        // After cleanup removes an expired token, TryRefresh throws ArgumentException
        var cleanedCredentials = new SitecoreAuthClientCredentials("cleaned", "secret");
        var expiredToClean = new SitecoreAuthToken("will-be-cleaned", DateTimeOffset.UtcNow.AddSeconds(-1));
        _tokenCache.SetToken(cleanedCredentials, expiredToClean);
        _tokenCache.PerformCleanup();
        await Should.ThrowAsync<ArgumentException>(() => _service.TryRefreshSitecoreAuthToken(expiredToClean));

        // 2. Refresh HTTP failure leaves old token removed from cache
        _tokenCache.ClearCache();
        _mockMessageHandler.SetResponses(
            TestHttpResponseHelper.CreateTokenResponse("before-failure"),
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var beforeFail = await _service.GetSitecoreAuthToken(_testCredentials);
        _tokenCache.CacheSize.ShouldBe(1);

        var httpEx = await Should.ThrowAsync<SitecoreAuthHttpException>(() => _service.TryRefreshSitecoreAuthToken(beforeFail));
        httpEx.StatusCode.ShouldBe(500);
        _tokenCache.CacheSize.ShouldBe(0);

        // 3. Direct non-DI cross-endpoint refresh throws ArgumentException without evicting owning endpoint
        using var sharedCache = new SitecoreTokenCache(Options.Create(new SitecoreTokenServiceOptions()));
        var svcA = new SitecoreTokenService(_httpClient, sharedCache, Options.Create(new SitecoreTokenServiceOptions { AuthTokenUrl = "https://endpoint-a.example.com/oauth/token" }));
        var svcB = new SitecoreTokenService(_httpClient, sharedCache, Options.Create(new SitecoreTokenServiceOptions { AuthTokenUrl = "https://endpoint-b.example.com/oauth/token" }));
        _mockMessageHandler.SetResponse(TestHttpResponseHelper.CreateTokenResponse("tok-endpoint-a"));
        var tokenA = await svcA.GetSitecoreAuthToken(_testCredentials);

        await Should.ThrowAsync<ArgumentException>(() => svcB.TryRefreshSitecoreAuthToken(tokenA));
        sharedCache.GetToken(_testCredentials, "https://endpoint-a.example.com/oauth/token").ShouldBe(tokenA);
    }

    [Fact]
    public async Task GetSitecoreAuthToken_ResponseAndTransportEdgeCases_ShouldThrowExpectedExceptionsWithoutCaching()
    {
        // 1. Missing access_token property
        _mockMessageHandler.SetResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"expires_in\":3600}", Encoding.UTF8, "application/json")
        });
        await Should.ThrowAsync<SitecoreAuthResponseException>(() => _service.GetSitecoreAuthToken(_testCredentials));

        // 2. Whitespace-only access_token property
        _mockMessageHandler.SetResponse(TestHttpResponseHelper.CreateTokenResponse("   "));
        await Should.ThrowAsync<SitecoreAuthResponseException>(() => _service.GetSitecoreAuthToken(_testCredentials));

        // 3. Missing expires_in property (defaults to 0 <= 0)
        _mockMessageHandler.SetResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"access_token\":\"tok\"}", Encoding.UTF8, "application/json")
        });
        await Should.ThrowAsync<SitecoreAuthResponseException>(() => _service.GetSitecoreAuthToken(_testCredentials));

        // 4. HTTP 401 and 500 preserve StatusCode and leave cache empty
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.InternalServerError })
        {
            _mockMessageHandler.SetResponse(new HttpResponseMessage(status));
            var httpEx = await Should.ThrowAsync<SitecoreAuthHttpException>(() => _service.GetSitecoreAuthToken(_testCredentials));
            httpEx.StatusCode.ShouldBe((int)status);
        }

        // 5. Transport-level HttpRequestException propagates
        using var failingHandler = new DelegateHttpMessageHandler(_ => throw new HttpRequestException("Network unreachable"));
        using var failingClient = new HttpClient(failingHandler);
        var failingService = new SitecoreTokenService(failingClient, _tokenCache, Options.Create(new SitecoreTokenServiceOptions()));
        await Should.ThrowAsync<HttpRequestException>(() => failingService.GetSitecoreAuthToken(_testCredentials));

        _tokenCache.CacheSize.ShouldBe(0);
    }

    [Fact]
    public async Task GenericSitecoreTokenService_DirectConstructionAndGuards_ShouldWorkAsExpected()
    {
        var options = Options.Create(new SitecoreTokenServiceOptions());
        var logger = Substitute.For<ILogger<SitecoreTokenService<SitecoreTokenServiceTests>>>();

        Should.Throw<ArgumentNullException>(() => new SitecoreTokenService<SitecoreTokenServiceTests>(null!, _tokenCache, options))
            .ParamName.ShouldBe("httpClient");
        Should.Throw<ArgumentNullException>(() => new SitecoreTokenService<SitecoreTokenServiceTests>(_httpClient, null!, options))
            .ParamName.ShouldBe("tokenCache");
        Should.Throw<ArgumentNullException>(() => new SitecoreTokenService<SitecoreTokenServiceTests>(_httpClient, _tokenCache, null!))
            .ParamName.ShouldBe("options");

        _mockMessageHandler.SetResponse(TestHttpResponseHelper.CreateTokenResponse("direct-typed-tok"));
        var typedService = new SitecoreTokenService<SitecoreTokenServiceTests>(_httpClient, _tokenCache, options, logger);
        typedService.ShouldBeAssignableTo<SitecoreTokenService>();
        typedService.ShouldBeAssignableTo<ISitecoreTokenService<SitecoreTokenServiceTests>>();

        var token = await typedService.GetSitecoreAuthToken(_testCredentials);
        token.AccessToken.ShouldBe("direct-typed-tok");
        logger.ShouldHaveLogged(LogLevel.Information, "Requesting new token");
    }

    public void Dispose()
    {
        _tokenCache?.Dispose();
        _httpClient?.Dispose();
        _mockMessageHandler?.Dispose();
    }

    private sealed class CancelingHttpContent(CancellationTokenSource cts) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            await cts.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
        }

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
        {
            cts.Cancel();
            return Task.FromCanceled<Stream>(cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    public class LegacyDispatchProxyCache : DispatchProxy
    {
        private readonly Dictionary<SitecoreAuthClientCredentials, SitecoreAuthToken> _store = new();
        public int OneArgGetCount { get; private set; }
        public int OneArgSetCount { get; private set; }
        public int OneArgRemoveCount { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(ISitecoreTokenCache.GetToken) when args?.Length == 1 => Get((SitecoreAuthClientCredentials)args[0]!),
                nameof(ISitecoreTokenCache.GetToken) when args?.Length == 2 => null,
                nameof(ISitecoreTokenCache.SetToken) when args?.Length == 2 => Set((SitecoreAuthClientCredentials)args[0]!, (SitecoreAuthToken)args[1]!),
                nameof(ISitecoreTokenCache.SetToken) when args?.Length == 3 => null,
                nameof(ISitecoreTokenCache.RemoveToken) when args?.Length == 1 => Remove((SitecoreAuthToken)args[0]!),
                nameof(ISitecoreTokenCache.RemoveToken) when args?.Length == 2 => null,
                nameof(ISitecoreTokenCache.Dispose) => null,
                _ => null
            };
        }

        private SitecoreAuthToken? Get(SitecoreAuthClientCredentials credentials)
        {
            OneArgGetCount++;
            return _store.TryGetValue(credentials, out var tok) ? tok : null;
        }

        private object? Set(SitecoreAuthClientCredentials credentials, SitecoreAuthToken token)
        {
            OneArgSetCount++;
            _store[credentials] = token;
            return null;
        }

        private SitecoreAuthClientCredentials? Remove(SitecoreAuthToken token)
        {
            OneArgRemoveCount++;
            foreach (var kvp in _store)
            {
                if (kvp.Value.Equals(token))
                {
                    _store.Remove(kvp.Key);
                    return kvp.Key;
                }
            }
            return null;
        }
    }
}

public class TestHttpMessageHandler : HttpMessageHandler
{
    private readonly Lock _syncLock = new();
    private readonly Queue<Func<Task<HttpResponseMessage>>> _responseFactories = new();
    private readonly List<HttpRequestMessage> _requests = new();
    private Func<Task<HttpResponseMessage>>? _defaultResponseFactory;

    public int RequestCount
    {
        get
        {
            lock (_syncLock) return _requests.Count;
        }
    }

    public HttpRequestMessage? LastRequest
    {
        get
        {
            lock (_syncLock) return _requests.LastOrDefault();
        }
    }

    public IReadOnlyList<HttpRequestMessage> Requests
    {
        get
        {
            lock (_syncLock) return _requests.ToArray();
        }
    }

    public void SetResponse(HttpResponseMessage response)
    {
        lock (_syncLock)
        {
            _responseFactories.Clear();
            _defaultResponseFactory = () => CloneResponseAsync(response);
        }
    }

    public void SetResponses(params HttpResponseMessage[] responses)
    {
        lock (_syncLock)
        {
            _responseFactories.Clear();
            _defaultResponseFactory = null;
            foreach (var response in responses)
            {
                var responseToClone = response;
                _responseFactories.Enqueue(() => CloneResponseAsync(responseToClone));
            }
        }
    }

    private static async Task<HttpResponseMessage> CloneResponseAsync(HttpResponseMessage original)
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
            var contentBytes = await original.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
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
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
        }

        Func<Task<HttpResponseMessage>> factory;
        lock (_syncLock)
        {
            _requests.Add(request);

            if (_responseFactories.Count > 0)
            {
                factory = _responseFactories.Dequeue();
            }
            else if (_defaultResponseFactory != null)
            {
                factory = _defaultResponseFactory;
            }
            else
            {
                throw new InvalidOperationException("No response configured for request");
            }
        }

        return factory();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_syncLock)
            {
                // Clear the queues but don't dispose original responses since they're managed by the test
                _responseFactories.Clear();

                foreach (var request in _requests)
                {
                    request?.Dispose();
                }
                _requests.Clear();
            }
        }
        base.Dispose(disposing);
    }
}

public class TimeoutHttpMessageHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout");
    }
}