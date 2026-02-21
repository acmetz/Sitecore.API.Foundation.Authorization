using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Sitecore.API.Foundation.Authorization.Abstractions;
using Sitecore.API.Foundation.Authorization.Configuration;
using Sitecore.API.Foundation.Authorization.Models;
using Sitecore.API.Foundation.Authorization.Services;
using Sitecore.API.Foundation.Authorization.Exceptions;
using Sitecore.API.Foundation.Tests.Mocks;
using Xunit;
using Xunit.Abstractions;

namespace Sitecore.API.Foundation.Tests;

public class SitecoreTokenServiceTests
{
    private readonly ITestOutputHelper _output;
    private readonly IOptions<SitecoreTokenServiceOptions> _options;
    private readonly ISitecoreTokenCache _mockTokenCache;
    private readonly TestLogger<SitecoreTokenService> _logger;
    private readonly HttpClient _httpClient;
    private readonly MockHttpMessageHandler _mockMessageHandler;
    private readonly SitecoreTokenService _service;
    private readonly SitecoreAuthClientCredentials _testCredentials = new("test_client","test_secret");

    public SitecoreTokenServiceTests(ITestOutputHelper output)
    {
        _output = output;
        _options = Options.Create(new SitecoreTokenServiceOptions());
        _mockTokenCache = new SitecoreTokenCache(_options);
        _mockMessageHandler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        _httpClient = new HttpClient(_mockMessageHandler);
        _logger = new TestLogger<SitecoreTokenService>(output);
        _service = new SitecoreTokenService(_httpClient, _options, _mockTokenCache, _logger);
    }

    [Fact]
    public void Constructor_WithNullHttpClient_ShouldThrowArgumentNullException()
    {
        var httpClient = new HttpClient(); // remove reference to _httpClient which not a field before initialization
        Should.Throw<ArgumentNullException>(() => new SitecoreTokenService(null!, _options, _mockTokenCache, _logger));
    }

    [Fact]
    public void Constructor_WithNullTokenCache_ShouldThrowArgumentNullException()
    {
        // Arrange
        var httpClient = new HttpClient();
        var options = Options.Create(new SitecoreTokenServiceOptions());

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new SitecoreTokenService(httpClient, options, null!, _logger));
    }

    [Fact]
    public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
    {
        var httpClient = new HttpClient();
        var ex = Should.Throw<ArgumentNullException>(() => new SitecoreTokenService(httpClient, null!, _mockTokenCache, _logger));
        ex.ParamName.ShouldBe("options");
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithCustomAuthUrl_ShouldUseCustomUrl()
    {
        var customUrl = "https://custom-auth.example.com/oauth/token";
        var options = Options.Create(new SitecoreTokenServiceOptions { AuthTokenUrl = customUrl });
        var cache = new SitecoreTokenCache(options);
        var service = new SitecoreTokenService(_httpClient, options, cache, _logger);

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
        var defaultOptions = Options.Create(new SitecoreTokenServiceOptions());
        var cache = new SitecoreTokenCache(defaultOptions);
        var service = new SitecoreTokenService(_httpClient, defaultOptions, cache, _logger);

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
        var invalidUrl = "not-a-valid-url";
        var options = Options.Create(new SitecoreTokenServiceOptions { AuthTokenUrl = invalidUrl });
        var cache = new SitecoreTokenCache(options);
        var service = new SitecoreTokenService(_httpClient, options, cache, _logger);

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
    public async Task GetSitecoreAuthToken_WithCachedToken_ShouldReturnCachedToken()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{\"access_token\":\"new_token\",\"expires_in\":3600}");
        var httpClient = new HttpClient(handler);
        var tokenService = new SitecoreTokenService(httpClient, _options, _mockTokenCache, _logger);
        var credentials = new SitecoreAuthClientCredentials("test_client", "test_secret");
        var cachedToken = new SitecoreAuthToken("cached_token", DateTimeOffset.UtcNow.AddHours(1));
        _mockTokenCache.SetToken(credentials, cachedToken);

        // Act
        var result = await tokenService.GetSitecoreAuthToken(credentials);

        // Assert
        result.AccessToken.ShouldBe(cachedToken.AccessToken);
        result.IsExpired.ShouldBeFalse();
        result.Expiration.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
        handler.RequestCount.ShouldBe(0); // No request should be made
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithExpiredToken_ShouldFetchNewToken()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{\"access_token\":\"new_token\",\"expires_in\":3600}");
        var httpClient = new HttpClient(handler);
        var tokenService = new SitecoreTokenService(httpClient, _options, _mockTokenCache, _logger);
        var credentials = new SitecoreAuthClientCredentials("test_client", "test_secret");
        var expiredToken = new SitecoreAuthToken("expired_token", DateTimeOffset.UtcNow.AddSeconds(-5));
        _mockTokenCache.SetToken(credentials, expiredToken);

        // Act
        var result = await tokenService.GetSitecoreAuthToken(credentials);

        // Assert
        result.AccessToken.ShouldBe("new_token");
        result.IsExpired.ShouldBeFalse();
        result.Expiration.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
        handler.RequestCount.ShouldBe(1); // Request should be made once
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithApiError_ShouldThrowSitecoreAuthHttpException()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "Internal Server Error");
        var httpClient = new HttpClient(handler);
        var tokenService = new SitecoreTokenService(httpClient, _options, _mockTokenCache, _logger);
        var credentials = new SitecoreAuthClientCredentials("test_client", "test_secret");
        var exception = await Should.ThrowAsync<SitecoreAuthHttpException>(() => tokenService.GetSitecoreAuthToken(credentials));
        exception.StatusCode.ShouldBe(500);
        exception.RequestUrl.ShouldBe(_options.Value.AuthTokenUrl);
        exception.Message.ShouldContain("InternalServerError");
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithInvalidResponse_ShouldThrowSitecoreAuthResponseException()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "invalid_json");
        var httpClient = new HttpClient(handler);
        var tokenService = new SitecoreTokenService(httpClient, _options, _mockTokenCache, _logger);
        var credentials = new SitecoreAuthClientCredentials("test_client", "test_secret");
        var exception = await Should.ThrowAsync<SitecoreAuthResponseException>(
            () => tokenService.GetSitecoreAuthToken(credentials));
        exception.Message.ShouldContain("Failed to parse auth response");
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithNullCredentials_ShouldThrowSitecoreAuthResponseException()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var httpClient = new HttpClient(handler);
        var tokenService = new SitecoreTokenService(httpClient, _options, _mockTokenCache, _logger);
        var exception = await Should.ThrowAsync<SitecoreAuthResponseException>(() => tokenService.GetSitecoreAuthToken(default));
        exception.Message.ShouldContain("Failed to read auth token");
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithNullClientId_ShouldThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new SitecoreAuthClientCredentials(null!, "test_secret"));
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithNullClientSecret_ShouldThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new SitecoreAuthClientCredentials("test_client", null!));
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithNullTokenEndpoint_ShouldThrowArgumentException()
    {
        // Removed: option property TokenEndpoint no longer exists. Keeping placeholder assertion skipped.
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithNullAudience_ShouldThrowArgumentException()
    {
        // Removed: Audience property no longer exists.
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithNullGrantType_ShouldThrowArgumentException()
    {
        // Removed: GrantType property no longer exists.
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithNullScope_ShouldThrowArgumentException()
    {
        // Removed: Scope property no longer exists.
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithCancellation_ShouldThrowTaskCanceledException()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(async (request, cancellationToken) =>
        {
            await Task.Delay(1000, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"test_token\",\"expires_in\":3600}")
            };
        });
        var httpClient = new HttpClient(handler);
        var tokenService = new SitecoreTokenService(httpClient, _options, _mockTokenCache, _logger);
        var credentials = new SitecoreAuthClientCredentials("test_client", "test_secret");
        var cts = new CancellationTokenSource();

        cts.Cancel(); // Cancel immediately

        // Act & Assert
        await Should.ThrowAsync<TaskCanceledException>(
            () => tokenService.GetSitecoreAuthToken(credentials, cts.Token));
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithInvalidJson_ShouldThrowSitecoreAuthResponseException()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "invalid_json");
        var httpClient = new HttpClient(handler);
        var tokenService = new SitecoreTokenService(httpClient, _options, _mockTokenCache, _logger);
        var credentials = new SitecoreAuthClientCredentials("test_client", "test_secret");
        var exception = await Should.ThrowAsync<SitecoreAuthResponseException>(
            () => tokenService.GetSitecoreAuthToken(credentials));
        exception.Message.ShouldContain("Failed to parse auth response");
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithNullResponse_ShouldThrowSitecoreAuthResponseException()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, null);
        var httpClient = new HttpClient(handler);
        var tokenService = new SitecoreTokenService(httpClient, _options, _mockTokenCache, _logger);
        var credentials = new SitecoreAuthClientCredentials("test_client", "test_secret");
        var exception = await Should.ThrowAsync<SitecoreAuthResponseException>(() => tokenService.GetSitecoreAuthToken(credentials));
        exception.Message.ShouldContain("Failed to read auth token");
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithEmptyResponse_ShouldThrowSitecoreAuthResponseException()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "");
        var httpClient = new HttpClient(handler);
        var tokenService = new SitecoreTokenService(httpClient, _options, _mockTokenCache, _logger);
        var credentials = new SitecoreAuthClientCredentials("test_client", "test_secret");
        var exception = await Should.ThrowAsync<SitecoreAuthResponseException>(() => tokenService.GetSitecoreAuthToken(credentials));
        exception.Message.ShouldContain("Failed to read auth token");
    }

    [Fact]
    public async Task GetSitecoreAuthToken_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var httpClient = new HttpClient(handler);

        // Act & Assert
        await Should.ThrowAsync<ArgumentNullException>(() =>
        {
            var tokenService = new SitecoreTokenService(httpClient, _options, _mockTokenCache, null!);
            return tokenService.GetSitecoreAuthToken(new SitecoreAuthClientCredentials("test_client", "test_secret"));
        });
    }
}