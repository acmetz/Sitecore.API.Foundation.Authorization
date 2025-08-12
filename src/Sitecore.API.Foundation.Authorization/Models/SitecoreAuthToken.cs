using System;

namespace Sitecore.API.Foundation.Authorization.Models;

/// <summary>
/// Represents an immutable Sitecore authentication token with expiration information.
/// </summary>
public readonly record struct SitecoreAuthToken(string AccessToken, DateTimeOffset Expiration)
{
    /// <summary>
    /// Gets the access token value.
    /// </summary>
    public string AccessToken { get; } = !string.IsNullOrEmpty(AccessToken) 
        ? AccessToken 
        : throw new ArgumentNullException(nameof(AccessToken));

    /// <summary>
    /// Gets the expiration date and time of the token.
    /// </summary>
    public DateTimeOffset Expiration { get; } = Expiration;

    /// <summary>
    /// Gets a value indicating whether the token has expired.
    /// </summary>
    public bool IsExpired => DateTimeOffset.UtcNow >= Expiration;
}