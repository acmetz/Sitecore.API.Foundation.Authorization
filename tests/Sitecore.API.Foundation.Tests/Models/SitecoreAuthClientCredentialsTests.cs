using System;
using Shouldly;
using Sitecore.API.Foundation.Authorization.Models;
using Xunit;

namespace Sitecore.API.Foundation.Tests.Models;

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

    [Fact]
    public void Equals_WithSameAndDifferentCredentials_ShouldUseValueEquality()
    {
        // Arrange
        var credentials1 = new SitecoreAuthClientCredentials("client-1", "secret-1");
        var credentials2 = new SitecoreAuthClientCredentials("client-1", "secret-1");
        var credentials3 = new SitecoreAuthClientCredentials("client-2", "secret-1");

        // Act & Assert
        credentials1.ShouldBe(credentials2);
        (credentials1 == credentials2).ShouldBeTrue();
        credentials1.GetHashCode().ShouldBe(credentials2.GetHashCode());
        credentials1.ShouldNotBe(credentials3);
        (credentials1 != credentials3).ShouldBeTrue();
    }

    [Fact]
    public void ToString_MasksClientSecret()
    {
        // Arrange
        var clientId = "test-client-id";
        var clientSecret = "super-secret-value";
        var credentials = new SitecoreAuthClientCredentials(clientId, clientSecret);

        // Act
        var result = credentials.ToString();

        // Assert
        result.ShouldBe($"SitecoreAuthClientCredentials {{ ClientId = {clientId}, ClientSecret = *** }}");
        result.ShouldContain($"ClientId = {clientId}");
        result.ShouldContain("ClientSecret = ***");
        result.ShouldNotContain(clientSecret);
    }
}
