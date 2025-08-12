using System;

namespace Sitecore.API.Foundation.Authorization.Configuration;

/// <summary>
/// Configuration options for the Sitecore token service and cache.
/// </summary>
public class SitecoreTokenServiceOptions
{
    /// <summary>
    /// Maximum number of tokens to cache. Default is 10.
    /// </summary>
    public int MaxCacheSize { get; set; } = 10;

    /// <summary>
    /// Number of tokens that triggers automatic cleanup. Default is 15.
    /// </summary>
    public int CleanupThreshold { get; set; } = 15;

    /// <summary>
    /// Interval between automatic cleanups. Default is 5 minutes.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The URL endpoint for obtaining Sitecore authentication tokens. 
    /// Defaults to the production Sitecore Cloud endpoint.
    /// Can be overridden for testing purposes.
    /// </summary>
    public string AuthTokenUrl { get; set; } = "https://auth.sitecorecloud.io/oauth/token";
}