using System;

namespace Sitecore.API.Foundation.Authorization.Exceptions;

/// <summary>
/// Base exception class for Sitecore authentication-related errors.
/// </summary>
public class SitecoreAuthException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SitecoreAuthException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public SitecoreAuthException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SitecoreAuthException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public SitecoreAuthException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when an HTTP request to Sitecore authentication services fails.
/// </summary>
public class SitecoreAuthHttpException : SitecoreAuthException
{
    /// <summary>
    /// Gets the HTTP status code returned by the authentication service.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// Gets the URL of the request that failed.
    /// </summary>
    public string? RequestUrl { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SitecoreAuthHttpException"/> class.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="requestUrl">The URL of the failed request.</param>
    /// <param name="message">The message that describes the error.</param>
    public SitecoreAuthHttpException(int statusCode, string? requestUrl, string message) : base(message)
    {
        StatusCode = statusCode;
        RequestUrl = requestUrl;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SitecoreAuthHttpException"/> class.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="requestUrl">The URL of the failed request.</param>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public SitecoreAuthHttpException(int statusCode, string? requestUrl, string message, Exception innerException) 
        : base(message, innerException)
    {
        StatusCode = statusCode;
        RequestUrl = requestUrl;
    }
}

/// <summary>
/// Exception thrown when the response from Sitecore authentication services cannot be parsed or is invalid.
/// </summary>
public class SitecoreAuthResponseException : SitecoreAuthException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SitecoreAuthResponseException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public SitecoreAuthResponseException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SitecoreAuthResponseException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public SitecoreAuthResponseException(string message, Exception innerException) : base(message, innerException)
    {
    }
}