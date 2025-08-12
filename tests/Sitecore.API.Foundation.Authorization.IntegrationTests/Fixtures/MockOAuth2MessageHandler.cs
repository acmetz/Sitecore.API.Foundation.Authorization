using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sitecore.API.Foundation.Authorization.IntegrationTests.Fixtures;

/// <summary>
/// Mock HTTP message handler that simulates OAuth2 server responses for integration testing.
/// This provides full integration test coverage without requiring Docker containers.
/// </summary>
public class MockOAuth2MessageHandler : HttpMessageHandler
{
    private readonly string _validClientId;
    private readonly string _validClientSecret;
    private readonly string _tokenEndpoint;
    private int _requestCount = 0;

    public MockOAuth2MessageHandler(string validClientId = "test-client", string validClientSecret = "test-secret", string tokenEndpoint = "http://localhost:8090/oauth/token")
    {
        _validClientId = validClientId;
        _validClientSecret = validClientSecret;
        _tokenEndpoint = tokenEndpoint;
    }

    /// <summary>
    /// Gets the number of requests that have been made to this handler.
    /// </summary>
    public int RequestCount => _requestCount;

    /// <summary>
    /// Gets the last request that was made to this handler.
    /// </summary>
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requestCount);
        LastRequest = request;

        // Handle health check requests
        if (request.RequestUri?.AbsolutePath == "/health")
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\": \"ok\"}", Encoding.UTF8, "application/json")
            };
        }

        // Handle token requests
        if (request.RequestUri?.AbsolutePath == "/oauth/token" && request.Method == HttpMethod.Post)
        {
            return await HandleTokenRequest(request);
        }

        // Default 404 for unknown endpoints
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> HandleTokenRequest(HttpRequestMessage request)
    {
        try
        {
            var content = await request.Content!.ReadAsStringAsync();
            
            // Parse the JSON request (Sitecore format)
            var requestData = JsonSerializer.Deserialize<JsonElement>(content);
            
            var grantType = requestData.TryGetProperty("grant_type", out var gtProp) ? gtProp.GetString() : null;
            var clientId = requestData.TryGetProperty("client_id", out var cidProp) ? cidProp.GetString() : null;
            var clientSecret = requestData.TryGetProperty("client_secret", out var csProp) ? csProp.GetString() : null;

            // Validate credentials
            if (grantType == "client_credentials" && clientId == _validClientId && clientSecret == _validClientSecret)
            {
                // Generate a realistic JWT-like token
                var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var guidValue = Guid.NewGuid().ToString("N");
                var expirationTime = currentTime + 3600;
                
                // Build token without complex string interpolation
                var tokenPrefix = "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9";
                var tokenPayload = $"eyJzdWIiOiJ0ZXN0LWNsaWVudCIsImV4cCI6{expirationTime}";
                var tokenSignature = $"mock-signature-{guidValue}";
                var token = $"{tokenPrefix}.{tokenPayload}.{tokenSignature}";
                
                var response = new
                {
                    access_token = token,
                    token_type = "Bearer",
                    expires_in = 3600
                };
                
                var jsonResponse = JsonSerializer.Serialize(response);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
                };
            }
            else
            {
                // Invalid credentials
                var errorResponse = new
                {
                    error = "invalid_client",
                    error_description = $"Invalid client credentials. Got: grant_type={grantType}, client_id={clientId}"
                };
                
                var jsonResponse = JsonSerializer.Serialize(errorResponse);
                return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
                };
            }
        }
        catch (Exception ex)
        {
            // Invalid request format
            var errorResponse = new
            {
                error = "invalid_request",
                error_description = $"Invalid request format: {ex.Message}"
            };
            
            var jsonResponse = JsonSerializer.Serialize(errorResponse);
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
            };
        }
    }
}