using DigitalBrain.Postgres;
using DigitalBrain.Sdk.Capacity;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Postgres.Tests.Unit;

public sealed class PostgresCapacityFacts
{
    [Fact]
    public async Task AComposedPostgresModuleResolvesThePlatformOriginForTables()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<PostgresModule>()
            .ConfigureSilo(silo => silo.Configuration["ConnectionStrings:postgres"] = "Host=localhost;Database=sample;Username=reader")
            .StartAsync(ct);
        var capacity = brain.SiloServices.GetRequiredService<ICapacity>();
        var resolved = await capacity.Resolve("postgres", new("brain-1", "app-1"), ct);
        Assert.Equal("platform", resolved.Origin);
    }

    [Fact]
    public async Task ProvisioningIsRefusedWhenOnlyThePlatformConnectionExists()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<PostgresModule>()
            .ConfigureSilo(silo => silo.Configuration["ConnectionStrings:postgres"] = "Host=localhost;Database=sample;Username=reader")
            .StartAsync(ct);
        var capacity = brain.SiloServices.GetRequiredService<ICapacity>();
        var refusal = await Assert.ThrowsAsync<CapacityUnavailableException>(
            async () => await capacity.Provision("postgres", new("brain-1", "app-1"), ct));
        Assert.Equal("Sorry, runtime provisioning is not accessible at the moment.", refusal.Message);
    }

    [Fact]
    public void TheRegistryAnswersThePlatformSourceForANullOrLegacyOrigin()
    {
        Assert.Equal(PostgresCapacityKind.PlatformOrigin, PostgresCapacityKind.OriginOrPlatform(null));
        Assert.Equal("db:brain_x", PostgresCapacityKind.OriginOrPlatform("db:brain_x"));
    }
}
