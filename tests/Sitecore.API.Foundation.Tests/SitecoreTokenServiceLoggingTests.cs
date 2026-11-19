using System;
using System.Net;
using System.Net.Http;
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

public class SitecoreTokenServiceLoggingTests
{
    private static SitecoreTokenService CreateService(
        HttpClient httpClient,
        ISitecoreTokenCache cache,
        ILogger<SitecoreTokenService>? logger = null,
        string authTokenUrl = "https://auth.test/token")
    {
        var options = Options.Create(new SitecoreTokenServiceOptions { AuthTokenUrl = authTokenUrl });
        return new SitecoreTokenService(httpClient, cache, options, logger);
    }

    [Fact]
    public async Task GetSitecoreAuthToken_UsesCacheAndLogsHit()
    {
        // Arrange
        var creds = new SitecoreAuthClientCredentials("id", "secret");
        var token = new SitecoreAuthToken("tok", DateTimeOffset.UtcNow.AddMinutes(5));
        using var cache = Substitute.For<ISitecoreTokenCache>();
        cache.GetToken(creds, Arg.Any<string>()).Returns(token);
        var logger = Substitute.For<ILogger<SitecoreTokenService>>();
        using var handler = new DelegateHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var svc = CreateService(httpClient, cache, logger);

        // Act
        var result = await svc.GetSitecoreAuthToken(creds);

        // Assert
        result.ShouldBe(token);
        logger.ShouldHaveLogged(LogLevel.Information, "Token cache hit");
    }

    [Fact]
    public async Task GetSitecoreAuthToken_Success_LogsRequestAndAcquisitionWithoutLeakingSecrets()
    {
        // Arrange
        var creds = new SitecoreAuthClientCredentials("happy-id", "super-secret-value");
        using var cache = Substitute.For<ISitecoreTokenCache>();
        cache.GetToken(creds, Arg.Any<string>()).Returns((SitecoreAuthToken?)null);
        var logger = Substitute.For<ILogger<SitecoreTokenService>>();
        using var handler = new DelegateHttpMessageHandler(_ => TestHttpResponseHelper.CreateTokenResponse("secret-bearer-token"));
        using var httpClient = new HttpClient(handler);
        var svc = CreateService(httpClient, cache, logger);

        // Act
        var result = await svc.GetSitecoreAuthToken(creds);

        // Assert
        result.AccessToken.ShouldBe("secret-bearer-token");
        logger.ShouldHaveLogged(LogLevel.Information, "Requesting new token for clientId happy-id");
        logger.ShouldHaveLogged(LogLevel.Debug, "Auth request payload:");
        logger.ShouldHaveLogged(LogLevel.Debug, "Auth response status: 200");
        logger.ShouldHaveLogged(LogLevel.Information, "Token acquired and cached");
        logger.ShouldNotHaveLogged("super-secret-value");
        logger.ShouldNotHaveLogged("secret-bearer-token");
    }

    [Fact]
    public async Task GetSitecoreAuthToken_HttpFailure_LogsWarningAndThrows()
    {
        // Arrange
        var creds = new SitecoreAuthClientCredentials("id", "secret");
        using var cache = Substitute.For<ISitecoreTokenCache>();
        cache.GetToken(creds, Arg.Any<string>()).Returns((SitecoreAuthToken?)null);
        var logger = Substitute.For<ILogger<SitecoreTokenService>>();
        using var handler = new DelegateHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("sensitive-error-body")
        });
        using var httpClient = new HttpClient(handler);
        var svc = CreateService(httpClient, cache, logger);

        // Act & Assert
        var ex = await Should.ThrowAsync<SitecoreAuthHttpException>(() => svc.GetSitecoreAuthToken(creds));
        ex.StatusCode.ShouldBe((int)HttpStatusCode.BadRequest);
        logger.ShouldHaveLogged(
            LogLevel.Warning,
            msg => msg.Contains("Authentication request failed") && !msg.Contains("sensitive-error-body"));
        logger.ShouldNotHaveLogged("sensitive-error-body");
    }

    [Fact]
    public async Task GetSitecoreAuthToken_ParseFailure_LogsErrorAndThrows()
    {
        // Arrange
        var creds = new SitecoreAuthClientCredentials("id", "secret");
        using var cache = Substitute.For<ISitecoreTokenCache>();
        cache.GetToken(creds, Arg.Any<string>()).Returns((SitecoreAuthToken?)null);
        var logger = Substitute.For<ILogger<SitecoreTokenService>>();
        using var handler = new DelegateHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ invalid json }")
        });
        using var httpClient = new HttpClient(handler);
        var svc = CreateService(httpClient, cache, logger);

        // Act & Assert
        await Should.ThrowAsync<SitecoreAuthResponseException>(() => svc.GetSitecoreAuthToken(creds));
        logger.ShouldHaveLogged(
            LogLevel.Error,
            msg => msg.Contains("Failed to parse authentication response") && !msg.Contains("invalid json"));
        logger.ShouldNotHaveLogged("invalid json");
    }

    [Fact]
    public async Task GetSitecoreAuthToken_EmptyAccessToken_LogsErrorAndDoesNotLogRawBody()
    {
        // Arrange
        var creds = new SitecoreAuthClientCredentials("id", "secret");
        using var cache = Substitute.For<ISitecoreTokenCache>();
        cache.GetToken(creds, Arg.Any<string>()).Returns((SitecoreAuthToken?)null);
        var logger = Substitute.For<ILogger<SitecoreTokenService>>();
        using var handler = new DelegateHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"access_token\":\"\",\"expires_in\":3600,\"secret_hint\":\"sensitive-raw-payload\"}")
        });
        using var httpClient = new HttpClient(handler);
        var svc = CreateService(httpClient, cache, logger);

        // Act & Assert
        await Should.ThrowAsync<SitecoreAuthResponseException>(() => svc.GetSitecoreAuthToken(creds));
        logger.ShouldHaveLogged(LogLevel.Error, "Authentication response was empty or missing access_token");
        logger.ShouldNotHaveLogged("sensitive-raw-payload");
    }

    [Fact]
    public async Task GetSitecoreAuthToken_NonPositiveExpiresIn_LogsErrorAndThrows()
    {
        // Arrange
        var creds = new SitecoreAuthClientCredentials("id", "secret");
        using var cache = Substitute.For<ISitecoreTokenCache>();
        cache.GetToken(creds, Arg.Any<string>()).Returns((SitecoreAuthToken?)null);
        var logger = Substitute.For<ILogger<SitecoreTokenService>>();
        using var handler = new DelegateHttpMessageHandler(_ => TestHttpResponseHelper.CreateTokenResponse("tok", expiresIn: 0));
        using var httpClient = new HttpClient(handler);
        var svc = CreateService(httpClient, cache, logger);

        // Act & Assert
        await Should.ThrowAsync<SitecoreAuthResponseException>(() => svc.GetSitecoreAuthToken(creds));
        logger.ShouldHaveLogged(LogLevel.Error, "Auth response contained non-positive expires_in");
    }

    [Fact]
    public async Task TryRefreshSitecoreAuthToken_UnmanagedToken_LogsWarningAndThrows()
    {
        // Arrange
        using var cache = Substitute.For<ISitecoreTokenCache>();
        var logger = Substitute.For<ILogger<SitecoreTokenService>>();
        using var handler = new DelegateHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var svc = CreateService(httpClient, cache, logger);

        // Act & Assert
        var token = new SitecoreAuthToken("unmanaged", DateTimeOffset.UtcNow.AddMinutes(5));
        await Should.ThrowAsync<ArgumentException>(() => svc.TryRefreshSitecoreAuthToken(token));
        logger.ShouldHaveLogged(LogLevel.Warning, "Attempted to refresh a token not managed by the service.");
    }

    [Fact]
    public async Task GetSitecoreAuthToken_UrlWithEmbeddedCredentials_LogsWarningWithoutLeakingCredentials()
    {
        // Arrange
        var creds = new SitecoreAuthClientCredentials("id", "secret");
        using var cache = Substitute.For<ISitecoreTokenCache>();
        cache.GetToken(creds, Arg.Any<string>()).Returns((SitecoreAuthToken?)null);
        var logger = Substitute.For<ILogger<SitecoreTokenService>>();
        using var handler = new DelegateHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var svc = CreateService(httpClient, cache, logger, "https://admin:super-secret-pass@auth.test/token");

        // Act & Assert
        await Should.ThrowAsync<SitecoreAuthException>(() => svc.GetSitecoreAuthToken(creds));
        logger.ShouldHaveLogged(LogLevel.Warning, "Invalid or insecure AuthTokenUrl");
        logger.ShouldNotHaveLogged(msg =>
            msg.Contains("super-secret-pass", StringComparison.Ordinal) ||
            msg.Contains("Requesting new token", StringComparison.Ordinal));
    }
}
