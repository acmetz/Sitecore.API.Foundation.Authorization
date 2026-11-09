using System;
using Shouldly;
using Sitecore.API.Foundation.Authorization.Exceptions;
using Xunit;

namespace Sitecore.API.Foundation.Tests;

public class SitecoreAuthExceptionTests
{
    [Fact]
    public void Constructor_WithMessage_SetsMessageProperty()
    {
        // Arrange
        var message = "Test error message";

        // Act
        var exception = new SitecoreAuthException(message);

        // Assert
        exception.Message.ShouldBe(message);
        exception.InnerException.ShouldBeNull();
    }

    [Fact]
    public void Constructor_WithMessageAndInnerException_SetsBothProperties()
    {
        // Arrange
        var message = "Test error message";
        var innerException = new Exception("Inner error");

        // Act
        var exception = new SitecoreAuthException(message, innerException);

        // Assert
        exception.Message.ShouldBe(message);
        exception.InnerException.ShouldBe(innerException);
    }
}

public class SitecoreAuthHttpExceptionTests
{
    [Fact]
    public void Constructor_WithAllParameters_SetsProperties()
    {
        // Arrange
        var statusCode = 404;
        var requestUrl = "https://example.com/api";
        var message = "Not Found";

        // Act
        var exception = new SitecoreAuthHttpException(statusCode, requestUrl, message);

        // Assert
        exception.StatusCode.ShouldBe(statusCode);
        exception.RequestUrl.ShouldBe(requestUrl);
        exception.Message.ShouldBe(message);
        exception.InnerException.ShouldBeNull();
    }

    [Fact]
    public void Constructor_WithAllParametersAndInnerException_SetsProperties()
    {
        // Arrange
        var statusCode = 500;
        var requestUrl = "https://example.com/api";
        var message = "Server Error";
        var innerException = new Exception("Network timeout");

        // Act
        var exception = new SitecoreAuthHttpException(statusCode, requestUrl, message, innerException);

        // Assert
        exception.StatusCode.ShouldBe(statusCode);
        exception.RequestUrl.ShouldBe(requestUrl);
        exception.Message.ShouldBe(message);
        exception.InnerException.ShouldBe(innerException);
    }
}

public class SitecoreAuthResponseExceptionTests
{
    [Fact]
    public void Constructor_WithMessage_SetsMessageProperty()
    {
        // Arrange
        var message = "Invalid response format";

        // Act
        var exception = new SitecoreAuthResponseException(message);

        // Assert
        exception.Message.ShouldBe(message);
        exception.InnerException.ShouldBeNull();
    }

    [Fact]
    public void Constructor_WithMessageAndInnerException_SetsBothProperties()
    {
        // Arrange
        var message = "Invalid response format";
        var innerException = new Exception("Parse error");

        // Act
        var exception = new SitecoreAuthResponseException(message, innerException);

        // Assert
        exception.Message.ShouldBe(message);
        exception.InnerException.ShouldBe(innerException);
    }
}
