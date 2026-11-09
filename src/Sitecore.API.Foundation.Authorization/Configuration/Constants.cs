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

    /// <summary>
    /// The logical name of the configured <see cref="System.Net.Http.HttpClient"/> registered in dependency injection.
    /// </summary>
    internal const string SitecoreHttpClientName = "Sitecore.API.Foundation.Authorization.HttpClient";
}