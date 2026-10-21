using Shouldly;
using Sitecore.API.Foundation.Authorization.Models;
using Xunit;

namespace Sitecore.API.Foundation.Tests.Models;

public class SitecoreAuthClientCredentialsTests
{
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
        result.ShouldContain($"ClientId = {clientId}");
        result.ShouldContain("ClientSecret = ***");
        result.ShouldNotContain(clientSecret);
    }
}
