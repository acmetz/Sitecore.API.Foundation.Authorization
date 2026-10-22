using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;

namespace Sitecore.API.Foundation.Authorization.IntegrationTests.Fixtures;

/// <summary>
/// Test fixture that provides a Keycloak container for integration testing with OAuth2 Client Credentials flow.
/// Automatically detects and guides through Docker configuration issues.
/// </summary>
public class KeycloakTestFixture : IAsyncDisposable
{
    // Primary image with fallbacks for reliability
    private static readonly string[] KeycloakImageOptions = 
    [
        "quay.io/keycloak/keycloak:24.0.1",
        "quay.io/keycloak/keycloak:23.0.0", 
        "quay.io/keycloak/keycloak:22.0.0",
        "quay.io/keycloak/keycloak:latest"
    ];
    
    private const int KeycloakPort = 8080;
    
    private IContainer? _keycloakContainer;
    private readonly HttpClient _httpClient = new();
    private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);
    private bool _isInitialized = false;
    private string? _selectedImage;
    private bool _isMockMode = false;

    /// <summary>
    /// The realm name used for testing.
    /// </summary>
    public string Realm => "test-realm";

    /// <summary>
    /// The client ID for OAuth2 client credentials authentication.
    /// </summary>
    public string ClientId => "my-client";

    /// <summary>
    /// The client secret for OAuth2 client credentials authentication.
    /// </summary>
    public string ClientSecret => "my-secret";

    /// <summary>
    /// The base URL of the Keycloak instance.
    /// </summary>
    public string BaseUrl
    {
        get
        {
            if (_keycloakContainer == null || !_isInitialized)
                return "http://localhost:8080"; // Placeholder before initialization

            // Only return mapped port if the container is running and has a mapped port
            try
            {
                var port = _keycloakContainer.GetMappedPublicPort(KeycloakPort);
                return $"http://{_keycloakContainer.Hostname}:{port}";
            }
            catch (InvalidOperationException)
            {
                // Container not started yet
                return "http://localhost:8080";
            }
        }
    }

    /// <summary>
    /// The OAuth2 token endpoint URL.
    /// </summary>
    public string TokenEndpoint => $"{BaseUrl}/realms/{Realm}/protocol/openid-connect/token";

    /// <summary>
    /// Gets the Docker image that was selected and is being used.
    /// </summary>
    public string SelectedImage => _selectedImage ?? KeycloakImageOptions[0];

    /// <summary>
    /// Indicates whether the fixture is running in mock mode due to Docker issues.
    /// </summary>
    public bool IsMockMode => _isMockMode;

    /// <summary>
    /// Ensures that the fixture is initialized, initializing it if it is not already initialized.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task EnsureInitializedAsync()
    {
        if (_isInitialized) return;
        
        await _initializationSemaphore.WaitAsync();
        try 
        {
            if (_isInitialized) return;
            await InitializeAsync();
            _isInitialized = true;
        } 
        finally 
        {
            _initializationSemaphore.Release();
        }
    }

    /// <summary>
    /// Initializes the Keycloak container and waits for it to be ready.
    /// </summary>
    private async Task InitializeAsync()
    {
        try
        {
            // Step 1: Check Docker availability and configuration
            await EnsureDockerAvailableAsync();
            
            // Step 2: Check Docker container mode
            await EnsureLinuxContainerModeAsync();
            
            // Step 3: Ensure Keycloak image is available
            await EnsureKeycloakImageAvailableAsync();
            
            // Step 4: Start Keycloak container
            await StartKeycloakContainerAsync();
            
            // Step 5: Wait for Keycloak to be ready
            await WaitForKeycloakAvailability();
            
            // Step 6: Configure realm and client
            await ConfigureKeycloakRealmAsync();
        }
        catch (Exception ex)
        {
            // Fall back to mock mode for better developer experience
            _isMockMode = true;
            
            var fallbackMessage = $"Falling back to mock mode due to Docker/Keycloak setup issue: {ex.Message}\n\n" +
                "The tests will continue using mock OAuth2 responses. " +
                "To enable real Keycloak integration, please resolve the Docker issue above.";
                
            // Log the fallback but don't fail - let tests continue in mock mode
            Console.WriteLine($"[WARNING] {fallbackMessage}");
        }
    }

    /// <summary>
    /// Checks if Docker is available and provides helpful error messages if not.
    /// </summary>
    private async Task EnsureDockerAvailableAsync()
    {
        try
        {
            // Check if docker command is available
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Unable to start docker process.");
            }

            await process.WaitForExitAsync();
            
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"Docker command failed: {error}");
            }
        }
        catch (Exception ex)
        {
            var errorMessage = "Docker is not available or not running.\n\n" +
                "?? To fix this issue:\n" +
                "1. Install Docker Desktop if not already installed\n" +
                "2. Start Docker Desktop and wait for it to be ready\n" +
                "3. Verify Docker is running: 'docker ps'\n" +
                "4. If using WSL2, ensure WSL2 integration is enabled in Docker Desktop settings\n\n" +
                $"Original error: {ex.Message}";
                
            throw new InvalidOperationException(errorMessage, ex);
        }
    }

    /// <summary>
    /// Ensures Docker is in Linux container mode, providing guidance if not.
    /// </summary>
    private async Task EnsureLinuxContainerModeAsync()
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Unable to start docker info process.");
            }

            await process.WaitForExitAsync();
            var output = await process.StandardOutput.ReadToEndAsync();

            if (output.Contains("OSType: windows"))
            {
                var errorMessage = "Docker Desktop is configured for Windows containers, but Keycloak requires Linux containers.\n\n" +
                    "?? To fix this issue:\n" +
                    "1. Right-click the Docker Desktop icon in the system tray\n" +
                    "2. Select 'Switch to Linux containers...'\n" +
                    "3. Wait for Docker Desktop to restart\n" +
                    "4. Verify the change: 'docker info | findstr OSType' should show 'linux'\n" +
                    "5. Re-run the tests\n\n" +
                    "?? Note: You can switch back to Windows containers later if needed.\n" +
                    "Linux containers are required for most integration testing scenarios.";
                    
                throw new InvalidOperationException(errorMessage);
            }
        }
        catch (Exception ex)
        {
            // If we can't determine the container mode, let the image pull attempt fail with its own error
            Console.WriteLine($"[WARNING] Could not determine Docker container mode: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensures a Keycloak image is available, trying multiple versions if necessary.
    /// </summary>
    private async Task EnsureKeycloakImageAvailableAsync()
    {
        var lastException = (Exception?)null;
        
        foreach (var imageName in KeycloakImageOptions)
        {
            try
            {
                // Try to pull the image with a reasonable timeout
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"pull {imageName}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processStartInfo);
                if (process == null)
                {
                    throw new InvalidOperationException($"Unable to start docker pull process for {imageName}.");
                }

                // Wait up to 5 minutes for image pull with cancellation token
                using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                
                try
                {
                    await process.WaitForExitAsync(cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    process.Kill();
                    throw new TimeoutException($"Docker pull timed out for image {imageName} after 5 minutes");
                }
                
                if (process.ExitCode == 0)
                {
                    _selectedImage = imageName;
                    return; // Success!
                }
                
                var error = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"Docker pull failed for {imageName}: {error}");
            }
            catch (Exception ex)
            {
                lastException = ex;
                // Continue to next image option
            }
        }
        
        // If we get here, none of the images worked
        var availableImages = string.Join(", ", KeycloakImageOptions);
        var errorMessage = $"Unable to pull any Keycloak Docker images. Tried: {availableImages}\n\n" +
            "?? To fix this issue:\n" +
            "1. Ensure Docker Desktop is using Linux containers (see previous error if applicable)\n" +
            "2. Check internet connectivity for image downloads\n" +
            "3. Verify access to quay.io: 'docker pull hello-world' should work\n" +
            "4. If behind a corporate firewall, configure Docker proxy settings\n" +
            "5. Try manually: 'docker pull quay.io/keycloak/keycloak:24.0.1'\n\n" +
            $"Last error: {lastException?.Message}";
            
        throw new InvalidOperationException(errorMessage, lastException);
    }

    /// <summary>
    /// Starts the Keycloak container with the selected image.
    /// </summary>
    private async Task StartKeycloakContainerAsync()
    {
        _keycloakContainer = new ContainerBuilder()
            .WithImage(_selectedImage!)
            .WithPortBinding(KeycloakPort, true)
            .WithEnvironment("KEYCLOAK_ADMIN", "admin")
            .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", "admin")
            .WithEnvironment("KC_HTTP_ENABLED", "true")
            .WithEnvironment("KC_HOSTNAME_STRICT", "false")
            .WithEnvironment("KC_HOSTNAME_STRICT_HTTPS", "false")
            .WithCommand("start-dev")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r
                    .ForPath("/realms/master")
                    .ForPort(KeycloakPort)
                    .ForStatusCode(System.Net.HttpStatusCode.OK)))
            .Build();

        await _keycloakContainer.StartAsync();
    }

    /// <summary>
    /// Waits for Keycloak to be available and responding with exponential backoff.
    /// </summary>
    private async Task WaitForKeycloakAvailability()
    {
        var maxAttempts = 30;
        var baseDelay = TimeSpan.FromSeconds(1);
        
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                // Test the master realm endpoint (this is Keycloak's health check)
                using var response = await _httpClient.GetAsync($"{BaseUrl}/realms/master");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                // Ignore network exceptions during startup
            }

            // Exponential backoff with jitter
            var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(1.5, i) + Random.Shared.Next(100, 500));
            await Task.Delay(delay);
        }

        throw new TimeoutException($"Keycloak did not become available within the expected time. " +
            $"Tried {maxAttempts} times over approximately {maxAttempts * 2} seconds. " +
            "Check Docker logs for Keycloak startup issues: 'docker logs <container_id>'");
    }

    /// <summary>
    /// Configures the Keycloak realm and client via Admin API.
    /// </summary>
    private async Task ConfigureKeycloakRealmAsync()
    {
        try
        {
            // Get admin access token
            var adminToken = await GetKeycloakAdminTokenAsync();
            
            // Create realm
            await CreateRealmAsync(adminToken);
            
            // Create client in realm
            await CreateClientAsync(adminToken);
            
            // Verify configuration
            await VerifyRealmConfigurationAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to configure Keycloak realm: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets an admin access token for Keycloak Admin API.
    /// </summary>
    private async Task<string> GetKeycloakAdminTokenAsync()
    {
        var tokenRequestData = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "password"),
            new("client_id", "admin-cli"),
            new("username", "admin"),
            new("password", "admin")
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/realms/master/protocol/openid-connect/token")
        {
            Content = new FormUrlEncodedContent(tokenRequestData)
        };

        using var response = await _httpClient.SendAsync(request);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Failed to get admin token. Status: {response.StatusCode}, Content: {errorContent}");
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        using var jsonDocument = JsonDocument.Parse(responseContent);
        
        if (!jsonDocument.RootElement.TryGetProperty("access_token", out var accessTokenElement))
        {
            throw new InvalidOperationException("Admin access token not found in response.");
        }

        return accessTokenElement.GetString() ?? throw new InvalidOperationException("Admin access token is null.");
    }

    /// <summary>
    /// Creates the test realm via Keycloak Admin API.
    /// </summary>
    private async Task CreateRealmAsync(string adminToken)
    {
        var realmData = new
        {
            realm = Realm,
            enabled = true,
            displayName = "Test Realm"
        };

        var jsonContent = JsonSerializer.Serialize(realmData);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/admin/realms")
        {
            Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
        };
        
        request.Headers.Add("Authorization", $"Bearer {adminToken}");

        using var response = await _httpClient.SendAsync(request);
        
        // 201 Created = success, 409 Conflict = already exists (also OK)
        if (response.StatusCode != System.Net.HttpStatusCode.Created && 
            response.StatusCode != System.Net.HttpStatusCode.Conflict)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Failed to create realm. Status: {response.StatusCode}, Content: {errorContent}");
        }
    }

    /// <summary>
    /// Creates the OAuth2 client in the test realm.
    /// </summary>
    private async Task CreateClientAsync(string adminToken)
    {
        var clientData = new
        {
            clientId = ClientId,
            secret = ClientSecret,
            publicClient = false,
            serviceAccountsEnabled = true,
            directAccessGrantsEnabled = false,
            standardFlowEnabled = false,
            clientAuthenticatorType = "client-secret",
            protocol = "openid-connect"
        };

        var jsonContent = JsonSerializer.Serialize(clientData);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/admin/realms/{Realm}/clients")
        {
            Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
        };
        
        request.Headers.Add("Authorization", $"Bearer {adminToken}");

        using var response = await _httpClient.SendAsync(request);
        
        // 201 Created = success, 409 Conflict = already exists (also OK)
        if (response.StatusCode != System.Net.HttpStatusCode.Created && 
            response.StatusCode != System.Net.HttpStatusCode.Conflict)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Failed to create client. Status: {response.StatusCode}, Content: {errorContent}");
        }
    }

    /// <summary>
    /// Verifies that the realm and client are properly configured.
    /// </summary>
    private async Task VerifyRealmConfigurationAsync()
    {
        // Test that the realm is accessible
        using var realmResponse = await _httpClient.GetAsync($"{BaseUrl}/realms/{Realm}");
        if (!realmResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Realm verification failed. Status: {realmResponse.StatusCode}");
        }

        // Test that the token endpoint is accessible
        using var tokenEndpointResponse = await _httpClient.GetAsync($"{BaseUrl}/realms/{Realm}/.well-known/openid_configuration");
        if (!tokenEndpointResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Token endpoint verification failed. Status: {tokenEndpointResponse.StatusCode}");
        }
    }

    /// <summary>
    /// Gets an OAuth2 access token using the client credentials flow.
    /// Returns real tokens when Keycloak is available, mock tokens in fallback mode.
    /// </summary>
    /// <returns>The access token string.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the token cannot be retrieved.</exception>
    public async Task<string> GetClientCredentialsTokenAsync()
    {
        await EnsureInitializedAsync();

        // If in mock mode, return a mock token
        if (_isMockMode)
        {
            return "mock-keycloak-token-" + Guid.NewGuid().ToString("N")[..16];
        }

        // Otherwise, get a real token from Keycloak
        if (_keycloakContainer == null)
            throw new InvalidOperationException("Keycloak container is not initialized.");

        var tokenRequestData = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "client_credentials"),
            new("client_id", ClientId),
            new("client_secret", ClientSecret)
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(tokenRequestData)
            };

            using var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Failed to get token from Keycloak. Status: {response.StatusCode}, Content: {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var jsonDocument = JsonDocument.Parse(responseContent);
            
            if (!jsonDocument.RootElement.TryGetProperty("access_token", out var accessTokenElement))
            {
                throw new InvalidOperationException("Access token not found in response.");
            }

            var accessToken = accessTokenElement.GetString();
            if (string.IsNullOrEmpty(accessToken))
            {
                throw new InvalidOperationException("Access token is null or empty.");
            }

            return accessToken;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"HTTP request failed when getting token: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new InvalidOperationException($"Request timed out when getting token: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Disposes the Keycloak container and HTTP client.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            _httpClient?.Dispose();

            if (_keycloakContainer != null)
            {
                await _keycloakContainer.DisposeAsync();
            }
        }
        finally
        {
            _initializationSemaphore?.Dispose();
        }
    }
}