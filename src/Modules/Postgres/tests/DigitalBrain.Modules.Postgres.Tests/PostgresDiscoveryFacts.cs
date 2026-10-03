using DigitalBrain.Postgres;

namespace DigitalBrain.Modules.Postgres.Tests;

public sealed class PostgresDiscoveryFacts
{
    [Fact]
    public async Task DiscoveryDescribesToolsWithoutEnumeratingAppTables()
    {
        // The provider has no database/index dependency: resource reads happen on demand.
        var result = await new PostgresRegistryResources().Discover(TestContext.Current.CancellationToken);
        Assert.Contains("postgres_schema", Assert.Single(result.Capabilities).Tools);
        Assert.Empty(result.Errors);
    }
}
