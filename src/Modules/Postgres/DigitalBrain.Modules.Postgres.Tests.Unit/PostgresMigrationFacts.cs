using System.Text.Json;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Postgres;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Postgres.Tests.Unit;

public sealed class PostgresMigrationFacts
{
    [Fact]
    public async Task LegacyEnumerationRunsOnceAndItsCompletionSurvivesReactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        var legacy = new LegacyTables();
        await using var brain = await UnitTest.Create().WithModule<PostgresModule>().ConfigureSilo(silo =>
        {
            silo.Configuration["ConnectionStrings:postgres"] = "Host=localhost;Database=sample;Username=reader";
            silo.Services.AddSingleton<IPostgresLegacyTables>(legacy);
        }).StartAsync(ct);
        CallerContextStamper.Stamp(new() { PrincipalId = "alice", AccountId = "alice", BrainId = "brain", Kind = CallerKind.Platform, StampedBy = TrustedEdge.Platform });
        var migration = brain.Get<IPostgresStorageMigration>("table-owners-v1");
        await migration.Ensure();
        await brain.DeactivateAsync(migration, ct);
        await migration.Ensure();
        Assert.Equal(1, legacy.Reads);
    }

    private sealed class LegacyTables : IPostgresLegacyTables
    {
        public int Reads { get; private set; }
        public Task<LegacyPostgresTable[]> Read(CancellationToken ct)
        {
            Reads++;
            return Task.FromResult<LegacyPostgresTable[]>([new("legacy", JsonSerializer.Serialize(new[] { BrainScope.Create("alice", "brain").Id, "app" }))]);
        }
    }
}
