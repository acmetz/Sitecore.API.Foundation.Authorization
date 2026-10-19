using System;
using Shouldly;
using Sitecore.API.Foundation.Authorization.Models;

namespace Sitecore.API.Foundation.Tests;

public class SitecoreAuthClientCredentialsTests
{
    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateCredentials()
    {
        // Arrange
        var clientId = "test-client-id";
        var clientSecret = "test-client-secret";

        // Act
        var credentials = new SitecoreAuthClientCredentials(clientId, clientSecret);

        // Assert
        credentials.ClientId.ShouldBe(clientId);
        credentials.ClientSecret.ShouldBe(clientSecret);
    }

    [Fact]
    public void Constructor_WithNullClientId_ShouldThrowArgumentNullException()
    {
        // Arrange
        var clientSecret = "test-client-secret";

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new SitecoreAuthClientCredentials(null!, clientSecret))
            .ParamName.ShouldBe("ClientId");
    }

    [Fact]
    public void Constructor_WithEmptyClientId_ShouldThrowArgumentNullException()
    {
        // Arrange
        var clientSecret = "test-client-secret";

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new SitecoreAuthClientCredentials("", clientSecret))
            .ParamName.ShouldBe("ClientId");
    }

    [Fact]
    public void Constructor_WithNullClientSecret_ShouldThrowArgumentNullException()
    {
        // Arrange
        var clientId = "test-client-id";

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new SitecoreAuthClientCredentials(clientId, null!))
            .ParamName.ShouldBe("ClientSecret");
    }

    [Fact]
    public void Constructor_WithEmptyClientSecret_ShouldThrowArgumentNullException()
    {
        // Arrange
        var clientId = "test-client-id";

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new SitecoreAuthClientCredentials(clientId, ""))
            .ParamName.ShouldBe("ClientSecret");
    }
}