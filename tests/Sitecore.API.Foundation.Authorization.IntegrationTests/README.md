# Integration Testing: Sitecore.API.Foundation.Authorization

## Prerequisites
- **.NET 10** (`net10.0`) and **.NET 11** (`net11.0`) SDKs installed
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
  - Start a Keycloak container using Testcontainers (`start-dev`)
  - Create and configure the test realm and client via the Keycloak Admin REST API (`ConfigureKeycloakRealmAsync`)
  - Clean up all containers after tests
  - Fall back to mock mode if Docker/Keycloak is unavailable

## Troubleshooting
- If tests are skipped or run in mock mode, check Docker logs and ensure:
  - Docker is running
  - Linux containers are enabled
  - Keycloak image can be pulled (`docker pull quay.io/keycloak/keycloak:24.0.1`)
- If tests hang or take too long:
  - Ensure Docker Desktop/Colima can bind an ephemeral host port (Testcontainers dynamically maps container port `8080` via `.WithPortBinding(8080, true)`)
  - Check for resource constraints
  - Inspect container logs using the Testcontainers-generated container ID from `docker ps -a`: `docker logs <container_id>`

Tests will quickly fall back to mock mode if Docker/Keycloak setup fails. All containers are cleaned up before and after runs.

If containers remain after a failed run, remove them manually:
  - `docker rm -f <container_id>` (check `docker ps -a`)

## Environment Variables
- No special environment variables are required for standard Docker Desktop.
- When running under Colima, ensure Testcontainers can reach the Docker socket (for example, set `TESTCONTAINERS_RYUK_DISABLED=true` in your environment if Ryuk cannot mount the socket; the test project does not set this automatically).

## Realm Configuration
- The test realm (`test-realm`) and confidential client (`my-client`) are provisioned programmatically via the Keycloak Admin REST API after the container starts (`ConfigureKeycloakRealmAsync`); `Realm/test-realm.json` is kept for reference only.
- No manual Keycloak configuration is required.

---
For more details, see the main project README.
