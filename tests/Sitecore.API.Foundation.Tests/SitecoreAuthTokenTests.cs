using System;
using System.Collections.Generic;
using Shouldly;
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Constructor_WithNullOrEmptyAccessToken_ShouldThrowArgumentNullException(string? invalidAccessToken)
    {
        // Arrange
        var expiration = DateTimeOffset.UtcNow.AddHours(1);

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new SitecoreAuthToken(invalidAccessToken!, expiration))
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
    public void IsExpired_WithNonUtcOffset_ShouldEvaluateAgainstUtcInstant()
    {
        // A local timestamp 2 hours ahead of UTC.Now at +05:00 represents 3 hours in the past in UTC.
        var wallClockFutureInUtcPast = new DateTimeOffset(DateTimeOffset.UtcNow.AddHours(2).DateTime, TimeSpan.FromHours(5));
        // A timestamp 1 hour ahead of UTC converted to -05:00 is still 1 hour in the future in UTC.
        var futureAtNegativeOffset = DateTimeOffset.UtcNow.AddHours(1).ToOffset(TimeSpan.FromHours(-5));

        new SitecoreAuthToken("past-token", wallClockFutureInUtcPast).IsExpired.ShouldBeTrue();
        new SitecoreAuthToken("future-token", futureAtNegativeOffset).IsExpired.ShouldBeFalse();
    }

    [Fact]
    public void IsExpired_WithBoundaryTimestamps_ShouldEvaluateWithoutOverflow()
    {
        new SitecoreAuthToken("min-token", DateTimeOffset.MinValue).IsExpired.ShouldBeTrue();
        new SitecoreAuthToken("max-token", DateTimeOffset.MaxValue).IsExpired.ShouldBeFalse();
    }

    [Fact]
    public void DefaultValue_ShouldHaveNullTokenAndBeExpired()
    {
        SitecoreAuthToken token = default;

        token.AccessToken.ShouldBeNull();
        token.Expiration.ShouldBe(default);
        token.IsExpired.ShouldBeTrue();
        token.ToString().ShouldBe($"SitecoreAuthToken {{ AccessToken = ***, Expiration = {default(DateTimeOffset)} }}");
    }

    [Fact]
    public void ValueEquality_WithMatchingAndDifferingValues_ShouldBehaveAsRecordStruct()
    {
        var expiration = DateTimeOffset.UtcNow.AddHours(1);
        var token1 = new SitecoreAuthToken("test-token", expiration);
        var sameToken = new SitecoreAuthToken("test-token", expiration);
        var differentAccessToken = new SitecoreAuthToken("other-token", expiration);
        var differentExpiration = new SitecoreAuthToken("test-token", expiration.AddHours(1));

        token1.Equals(sameToken).ShouldBeTrue();
        token1.Equals((object)sameToken).ShouldBeTrue();
        (token1 == sameToken).ShouldBeTrue();
        (token1 != sameToken).ShouldBeFalse();
        token1.GetHashCode().ShouldBe(sameToken.GetHashCode());

        token1.Equals(differentAccessToken).ShouldBeFalse();
        (token1 != differentAccessToken).ShouldBeTrue();
        token1.Equals(differentExpiration).ShouldBeFalse();
        (token1 != differentExpiration).ShouldBeTrue();
        token1.Equals((object?)null).ShouldBeFalse();
        token1.Equals("not a token").ShouldBeFalse();

        SitecoreAuthToken? nullableA = null;
        SitecoreAuthToken? nullableB = null;
        (nullableA == nullableB).ShouldBeTrue();
        (nullableA == token1).ShouldBeFalse();
        (token1 != nullableA).ShouldBeTrue();
    }

    [Fact]
    public void HashSet_ShouldWorkCorrectlyWithTokens()
    {
        // Arrange
        var expiration = DateTimeOffset.UtcNow.AddHours(1);
        var token1 = new SitecoreAuthToken("test-token", expiration);
        var token2 = new SitecoreAuthToken("test-token", expiration); // Same as token1
        var token3 = new SitecoreAuthToken("different-token", expiration);

        // Act
        HashSet<SitecoreAuthToken> hashSet = [token1, token2, token3];

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

    [Fact]
    public void ToString_ShouldMaskAccessToken()
    {
        // Arrange
        var accessToken = "super-secret-bearer-token";
        var expiration = DateTimeOffset.UtcNow.AddHours(1);
        var token = new SitecoreAuthToken(accessToken, expiration);

        // Act
        var formatted = token.ToString();

        // Assert
        formatted.ShouldBe($"SitecoreAuthToken {{ AccessToken = ***, Expiration = {expiration} }}");
        formatted.ShouldNotContain(accessToken);
    }
}