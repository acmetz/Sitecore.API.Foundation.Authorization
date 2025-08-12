using System;
using System.Collections.Generic;
using Shouldly;
using Sitecore.API.Foundation.Authorization;
using Sitecore.API.Foundation.Authorization.Models;

namespace Sitecore.API.Foundation.Tests;

public class SitecoreAuthTokenTests
{
    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateToken()
    {
        // Arrange
        var accessToken = "test-token";
        var expiration = DateTimeOffset.UtcNow.AddHours(1);

        // Act
        var token = new SitecoreAuthToken(accessToken, expiration);

        // Assert
        token.AccessToken.ShouldBe(accessToken);
        token.Expiration.ShouldBe(expiration);
        token.IsExpired.ShouldBeFalse();
    }

    [Fact]
    public void Constructor_WithNullAccessToken_ShouldThrowArgumentNullException()
    {
        // Arrange
        var expiration = DateTimeOffset.UtcNow.AddHours(1);

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new SitecoreAuthToken(null!, expiration))
            .ParamName.ShouldBe("AccessToken");
    }

    [Fact]
    public void Constructor_WithEmptyAccessToken_ShouldThrowArgumentNullException()
    {
        // Arrange
        var expiration = DateTimeOffset.UtcNow.AddHours(1);

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new SitecoreAuthToken("", expiration))
            .ParamName.ShouldBe("AccessToken");
    }

    [Fact]
    public void IsExpired_WithFutureExpiration_ShouldReturnFalse()
    {
        // Arrange
        var token = new SitecoreAuthToken("test-token", DateTimeOffset.UtcNow.AddHours(1));

        // Act & Assert
        token.IsExpired.ShouldBeFalse();
    }

    [Fact]
    public void IsExpired_WithPastExpiration_ShouldReturnTrue()
    {
        // Arrange
        var token = new SitecoreAuthToken("test-token", DateTimeOffset.UtcNow.AddHours(-1));

        // Act & Assert
        token.IsExpired.ShouldBeTrue();
    }

    [Fact]
    public void Equals_WithSameTokens_ShouldReturnTrue()
    {
        // Arrange
        var expiration = DateTimeOffset.UtcNow.AddHours(1);
        var token1 = new SitecoreAuthToken("test-token", expiration);
        var token2 = new SitecoreAuthToken("test-token", expiration);

        // Act & Assert
        token1.Equals(token2).ShouldBeTrue();
        token1.Equals((object)token2).ShouldBeTrue();
    }

    [Fact]
    public void Equals_WithDifferentTokens_ShouldReturnFalse()
    {
        // Arrange
        var expiration = DateTimeOffset.UtcNow.AddHours(1);
        var token1 = new SitecoreAuthToken("test-token-1", expiration);
        var token2 = new SitecoreAuthToken("test-token-2", expiration);

        // Act & Assert
        token1.Equals(token2).ShouldBeFalse();
        token1.Equals((object)token2).ShouldBeFalse();
    }

    [Fact]
    public void Equals_WithDifferentExpirations_ShouldReturnFalse()
    {
        // Arrange
        var token1 = new SitecoreAuthToken("test-token", DateTimeOffset.UtcNow.AddHours(1));
        var token2 = new SitecoreAuthToken("test-token", DateTimeOffset.UtcNow.AddHours(2));

        // Act & Assert
        token1.Equals(token2).ShouldBeFalse();
    }

    [Fact]
    public void Equals_WithNull_ShouldReturnFalse()
    {
        // Arrange
        var token = new SitecoreAuthToken("test-token", DateTimeOffset.UtcNow.AddHours(1));

        // Act & Assert
        token.Equals(null).ShouldBeFalse();
        token.Equals((object?)null).ShouldBeFalse();
    }

    [Fact]
    public void Equals_WithSameReference_ShouldReturnTrue()
    {
        // Arrange
        var token = new SitecoreAuthToken("test-token", DateTimeOffset.UtcNow.AddHours(1));

        // Act & Assert
        token.Equals(token).ShouldBeTrue();
        token.Equals((object)token).ShouldBeTrue();
    }

    [Fact]
    public void Equals_WithDifferentType_ShouldReturnFalse()
    {
        // Arrange
        var token = new SitecoreAuthToken("test-token", DateTimeOffset.UtcNow.AddHours(1));
        var otherObject = "not a token";

        // Act & Assert
        token.Equals(otherObject).ShouldBeFalse();
    }

    [Fact]
    public void GetHashCode_WithSameTokens_ShouldReturnSameHashCode()
    {
        // Arrange
        var expiration = DateTimeOffset.UtcNow.AddHours(1);
        var token1 = new SitecoreAuthToken("test-token", expiration);
        var token2 = new SitecoreAuthToken("test-token", expiration);

        // Act & Assert
        token1.GetHashCode().ShouldBe(token2.GetHashCode());
    }

    [Fact]
    public void GetHashCode_WithDifferentTokens_ShouldReturnDifferentHashCodes()
    {
        // Arrange
        var expiration = DateTimeOffset.UtcNow.AddHours(1);
        var token1 = new SitecoreAuthToken("test-token-1", expiration);
        var token2 = new SitecoreAuthToken("test-token-2", expiration);

        // Act & Assert
        token1.GetHashCode().ShouldNotBe(token2.GetHashCode());
    }

    [Fact]
    public void OperatorEquals_WithSameTokens_ShouldReturnTrue()
    {
        // Arrange
        var expiration = DateTimeOffset.UtcNow.AddHours(1);
        var token1 = new SitecoreAuthToken("test-token", expiration);
        var token2 = new SitecoreAuthToken("test-token", expiration);

        // Act & Assert
        (token1 == token2).ShouldBeTrue();
        (token1 != token2).ShouldBeFalse();
    }

    [Fact]
    public void OperatorEquals_WithDifferentTokens_ShouldReturnFalse()
    {
        // Arrange
        var expiration = DateTimeOffset.UtcNow.AddHours(1);
        var token1 = new SitecoreAuthToken("test-token-1", expiration);
        var token2 = new SitecoreAuthToken("test-token-2", expiration);

        // Act & Assert
        (token1 == token2).ShouldBeFalse();
        (token1 != token2).ShouldBeTrue();
    }

    [Fact]
    public void OperatorEquals_WithNulls_ShouldHandleCorrectly()
    {
        // Arrange
        SitecoreAuthToken? token1 = null;
        SitecoreAuthToken? token2 = null;
        var token3 = new SitecoreAuthToken("test-token", DateTimeOffset.UtcNow.AddHours(1));

        // Act & Assert
        (token1 == token2).ShouldBeTrue(); // Both null
        (token1 != token2).ShouldBeFalse();
        
        (token1 == token3).ShouldBeFalse(); // One null
        (token1 != token3).ShouldBeTrue();
        
        (token3 == token1).ShouldBeFalse(); // One null (reversed)
        (token3 != token1).ShouldBeTrue();
    }

    [Fact]
    public void HashSet_ShouldWorkCorrectlyWithTokens()
    {
        // Arrange
        var expiration = DateTimeOffset.UtcNow.AddHours(1);
        var token1 = new SitecoreAuthToken("test-token", expiration);
        var token2 = new SitecoreAuthToken("test-token", expiration); // Same as token1
        var token3 = new SitecoreAuthToken("different-token", expiration);

        var hashSet = new HashSet<SitecoreAuthToken>();

        // Act
        hashSet.Add(token1);
        hashSet.Add(token2); // Should not add duplicate
        hashSet.Add(token3);

        // Assert
        hashSet.Count.ShouldBe(2); // Only token1 and token3
        hashSet.Contains(token1).ShouldBeTrue();
        hashSet.Contains(token2).ShouldBeTrue(); // token2 equals token1
        hashSet.Contains(token3).ShouldBeTrue();
    }

    [Fact]
    public void Dictionary_ShouldWorkCorrectlyWithTokensAsKeys()
    {
        // Arrange
        var expiration = DateTimeOffset.UtcNow.AddHours(1);
        var token1 = new SitecoreAuthToken("test-token", expiration);
        var token2 = new SitecoreAuthToken("test-token", expiration); // Same as token1
        var token3 = new SitecoreAuthToken("different-token", expiration);

        var dictionary = new Dictionary<SitecoreAuthToken, string>();

        // Act
        dictionary[token1] = "value1";
        dictionary[token2] = "value2"; // Should update token1's value
        dictionary[token3] = "value3";

        // Assert
        dictionary.Count.ShouldBe(2); // Only token1 and token3 as keys
        dictionary[token1].ShouldBe("value2"); // Updated by token2
        dictionary[token2].ShouldBe("value2"); // Same as token1
        dictionary[token3].ShouldBe("value3");
    }
}