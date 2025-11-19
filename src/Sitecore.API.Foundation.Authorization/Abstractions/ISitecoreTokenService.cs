using System;
using System.Threading;
using System.Threading.Tasks;
using Sitecore.API.Foundation.Authorization.Models;

namespace Sitecore.API.Foundation.Authorization.Abstractions;

/// <summary>
/// Interface for managing Sitecore authentication token service operations.
/// </summary>
public interface ISitecoreTokenService
{
    /// <summary>
    /// Gets a Sitecore authentication token for the specified credentials.
    /// </summary>
    /// <param name="credentials">The client credentials to authenticate with.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation containing the authentication token.</returns>
    Task<SitecoreAuthToken> GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Refreshes an existing Sitecore authentication token.
    /// </summary>
    /// <param name="token">The token to refresh.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation containing the refreshed token.</returns>
    Task<SitecoreAuthToken> TryRefreshSitecoreAuthToken(SitecoreAuthToken token, CancellationToken cancellationToken = default);
}