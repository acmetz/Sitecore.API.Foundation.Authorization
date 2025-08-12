# Integration Testing: Sitecore.API.Foundation.Authorization

## Prerequisites
- **Docker Desktop** (or Colima) must be installed and running
- Linux containers must be enabled in Docker Desktop
- Internet access to pull Keycloak images from quay.io
- Sufficient resources for running containers

## Running Integration Tests
1. Ensure Docker is running and healthy:
   - `docker ps` should show running containers
   - If using Colima, ensure Colima is started
2. Run all tests using your preferred .NET test runner (e.g. `dotnet test`)
3. The tests will automatically:
  - Start a Keycloak container using Testcontainers
  - Import the test realm and client
  - Clean up all containers after tests
  - Fall back to mock mode if Docker/Keycloak is unavailable

## Troubleshooting
- If tests are skipped or run in mock mode, check Docker logs and ensure:
  - Docker is running
  - Linux containers are enabled
  - Keycloak image can be pulled (`docker pull quay.io/keycloak/keycloak:24.0.1`)
- If tests hang or take too long:
  - Ensure port 8080 is free
  - Check for resource constraints
  - Inspect container logs: `docker logs test-keycloak-integration`

Tests will quickly fall back to mock mode if Docker/Keycloak setup fails. All containers are cleaned up before and after runs.

If containers remain after a failed run, remove them manually:
  - `docker rm -f test-keycloak-integration`

## Environment Variables
- No special environment variables required for standard Docker Desktop
- For Colima, `TESTCONTAINERS_RYUK_DISABLED=true` is set automatically

## Realm Import
- The test realm and client are imported automatically at container startup
- No manual Keycloak configuration required

---
For more details, see the main project README.
