using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Sitecore.API.Foundation.Authorization.IntegrationTests.Fixtures;

/// <summary>
/// Mock OAuth2 test fixture that simulates a live OAuth2 server using HTTP message handlers.
/// This provides full integration testing without requiring Docker containers.
/// </summary>
public class MockOAuth2TestFixture : IAsyncDisposable
{
    private bool _isInitialized = false;
    private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);
    private MockOAuth2MessageHandler? _messageHandler;
    private HttpClient? _httpClient;

    /// <summary>
    /// The client ID for OAuth2 client credentials authentication.
    /// </summary>
    public string ClientId => "test-client";

    /// <summary>
    /// The client secret for OAuth2 client credentials authentication.
    /// </summary>
    public string ClientSecret => "test-secret";

    /// <summary>
    /// The base URL of the mock OAuth2 server.
    /// </summary>
    public string BaseUrl => "http://localhost:8090";

    /// <summary>
    /// The OAuth2 token endpoint URL that accepts Sitecore's JSON format.
    /// </summary>
    public string TokenEndpoint => $"{BaseUrl}/oauth/token";

    /// <summary>
    /// Gets the mock message handler for inspection and verification.
    /// Only available after initialization.
    /// </summary>
    public MockOAuth2MessageHandler MessageHandler
    {
        get
        {
            if (_messageHandler == null)
                throw new InvalidOperationException("MockOAuth2TestFixture is not initialized. Call EnsureInitializedAsync() first.");
            return _messageHandler;
        }
    }

    /// <summary>
    /// Gets the HttpClient configured with the mock handler.
    /// Creates a temporary client if not initialized yet.
    /// </summary>
    public HttpClient HttpClient
    {
        get
        {
            if (_httpClient != null)
                return _httpClient;
                
            // Create a temporary client if not initialized yet
            // This will be replaced during proper initialization
            if (_messageHandler == null)
            {
                _messageHandler = new MockOAuth2MessageHandler(ClientId, ClientSecret);
            }
            
            if (_httpClient == null)
            {
                _httpClient = new HttpClient(_messageHandler);
            }
            
            return _httpClient;
        }
    }

    /// <summary>
    /// Ensures the mock OAuth2 server is initialized and ready for testing.
    /// This method is safe to call multiple times.
    /// </summary>
    public async Task EnsureInitializedAsync()
    {
        if (_isInitialized)
            return;

        await _initializationSemaphore.WaitAsync();
        try
        {
            if (_isInitialized)
                return;

            await InitializeAsync();
            _isInitialized = true;
        }
        finally
        {
            _initializationSemaphore.Release();
        }
    }

    /// <summary>
    /// Initializes the mock OAuth2 server.
    /// </summary>
    private Task InitializeAsync()
    {
        try
        {
            // If we already have a client from the getter, we're good to go
            _messageHandler ??= new MockOAuth2MessageHandler(ClientId, ClientSecret);
            _httpClient ??= new HttpClient(_messageHandler);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to initialize mock OAuth2 server: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Simulates a health check against the mock OAuth2 server.
    /// </summary>
    public async Task<bool> IsHealthyAsync()
    {
        await EnsureInitializedAsync();
        
        try
        {
            using var response = await HttpClient.GetAsync($"{BaseUrl}/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Disposes the mock OAuth2 server resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            _httpClient?.Dispose();
            _messageHandler?.Dispose();
        }
        finally
        {
            _initializationSemaphore?.Dispose();
        }
        
        await Task.CompletedTask;
    }
}