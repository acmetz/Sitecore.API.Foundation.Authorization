using Xunit;

namespace Sitecore.API.Foundation.Authorization.IntegrationTests.Fixtures;

/// <summary>
/// Collection definition to ensure that Keycloak container tests don't run in parallel
/// to avoid port conflicts and resource contention.
/// </summary>
[CollectionDefinition("Keycloak Collection")]
public class KeycloakCollectionDefinition : ICollectionFixture<KeycloakTestFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}