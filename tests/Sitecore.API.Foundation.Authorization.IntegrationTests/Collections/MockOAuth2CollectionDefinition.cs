using Sitecore.API.Foundation.Authorization.IntegrationTests.Fixtures;
using Xunit;

namespace Sitecore.API.Foundation.Authorization.IntegrationTests.Collections;

/// <summary>
/// Test collection definition for mock OAuth2 integration tests.
/// This ensures that mock-based tests run sequentially to avoid interference.
/// </summary>
[CollectionDefinition("Mock OAuth2 Collection")]
public class MockOAuth2CollectionDefinition : ICollectionFixture<MockOAuth2TestFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}