namespace Sitecore.API.Foundation.Authorization.Configuration;

/// <summary>
/// Internal constants for Sitecore authentication endpoints and configuration.
/// </summary>
internal static class Constants
{
    /// <summary>
    /// The audience identifier for Sitecore authentication requests.
    /// </summary>
    internal const string SitecoreAuthAudience = "https://api.sitecorecloud.io";
    
    /// <summary>
    /// The grant type used for Sitecore authentication.
    /// </summary>
    internal const string SitecoreAuthGrantType = "client_credentials";
    
    /// <summary>
    /// The URL endpoint for obtaining Sitecore authentication tokens.
    /// </summary>
    internal const string SitecoreCloudAuthTokenUrl = "https://auth.sitecorecloud.io/oauth/token";
}