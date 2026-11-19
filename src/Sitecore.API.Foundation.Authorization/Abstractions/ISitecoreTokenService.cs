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
    /// <returns>A task that represents the asynchronous operation containing the authentication token.</returns>
    Task<SitecoreAuthToken> GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials);

    /// <summary>
    /// Gets a Sitecore authentication token for the specified credentials.
    /// </summary>
    /// <param name="credentials">The client credentials to authenticate with.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation containing the authentication token.</returns>
    Task<SitecoreAuthToken> GetSitecoreAuthToken(SitecoreAuthClientCredentials credentials, CancellationToken cancellationToken);
    
    /// <summary>
    /// Refreshes an existing Sitecore authentication token.
    /// </summary>
    /// <param name="token">The token to refresh.</param>
    /// <returns>A task that represents the asynchronous operation containing the refreshed token.</returns>
    Task<SitecoreAuthToken> TryRefreshSitecoreAuthToken(SitecoreAuthToken token);

    /// <summary>
    /// Refreshes an existing Sitecore authentication token.
    /// </summary>
    /// <param name="token">The token to refresh.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation containing the refreshed token.</returns>
    Task<SitecoreAuthToken> TryRefreshSitecoreAuthToken(SitecoreAuthToken token, CancellationToken cancellationToken);
}

/// <summary>
/// Typed interface for managing Sitecore authentication token service operations for a specific consumer type <typeparamref name="TClient"/>.
/// </summary>
/// <typeparam name="TClient">The consumer type whose named <see cref="System.Net.Http.HttpClient"/> and options are used.</typeparam>
public interface ISitecoreTokenService<TClient> : ISitecoreTokenService where TClient : class
{
}