using System;

namespace Sitecore.API.Foundation.Authorization.Models;

/// <summary>
/// Represents immutable client credentials for Sitecore authentication.
/// </summary>
public readonly record struct SitecoreAuthClientCredentials(string ClientId, string ClientSecret)
{
    /// <summary>
    /// Gets the client identifier.
    /// </summary>
    public string ClientId { get; } = !string.IsNullOrEmpty(ClientId) 
        ? ClientId 
        : throw new ArgumentNullException(nameof(ClientId));

    /// <summary>
    /// Gets the client secret.
    /// </summary>
    public string ClientSecret { get; } = !string.IsNullOrEmpty(ClientSecret) 
        ? ClientSecret 
        : throw new ArgumentNullException(nameof(ClientSecret));
}