using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Storage;

namespace DigitalBrain.Modules.Postgres.Tests.Unit;

public sealed class LegacyPostgresStateFacts
{
    [Fact]
    public async Task StartupBackfillReclaimsALegacyTableWithoutAnyNewDefine()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new PostgresWriteTableFacts.MemoryTables();
        await using var brain = await UnitTest.Create().WithModule<PostgresModule>().ConfigureSilo(silo =>
        {
            silo.Configuration["ConnectionStrings:postgres"] = "Host=localhost;Database=sample;Username=reader";
            silo.Services.AddSingleton<IPostgresTableProvider>(provider);
            silo.Services.AddSingleton<IPostgresLegacyTables>(new UntouchedLegacyTable());
            silo.Services.AddKeyedSingleton<IGrainStorage>(DigitalBrainNames.DefaultGrainStorage, (services, _) =>
                new LegacyStorage(new OrleansGrainStorageSerializer(services.GetRequiredService<Serializer>())));
        }).StartAsync(ct);
        CallerContextStamper.Stamp(new() { PrincipalId = "alice", AccountId = "alice", BrainId = "brain", AppId = "app", Kind = CallerKind.App, StampedBy = TrustedEdge.AppProxy });

        var app = brain.Get<IPostgresLifecycleTestApp>("untouched-install");
        await app.Uninstall("brain", "app");
        await app.Uninstall("brain", "app");

        Assert.Equal(("platform", "legacy_table"), Assert.Single(provider.Drops));
        Assert.Equal(0, provider.Definitions);
        var storage = (LegacyStorage)brain.SiloServices.GetRequiredKeyedService<IGrainStorage>(DigitalBrainNames.DefaultGrainStorage);
        Assert.Null(storage.Last!.Owner);
        Assert.Null(storage.Last.Accepted);
    }

    private sealed class UntouchedLegacyTable : IPostgresLegacyTables
    {
        public Task<LegacyPostgresTable[]> Read(CancellationToken ct)
            => Task.FromResult<LegacyPostgresTable[]>([new("legacy", System.Text.Json.JsonSerializer.Serialize(new[] { BrainScope.Create("alice", "brain").Id, "app" }))]);
    }

    [Fact]
    public async Task LegacyNullOriginStateReactivatesAndTeardownRetriesAfterTheDropWasSavedOnlyInPostgres()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new PostgresWriteTableFacts.MemoryTables();
        await using var brain = await UnitTest.Create().WithModule<PostgresModule>().ConfigureSilo(silo =>
        {
            silo.Configuration["ConnectionStrings:postgres"] = "Host=localhost;Database=sample;Username=reader";
            silo.Services.AddSingleton<IPostgresTableProvider>(provider);
            silo.Services.AddKeyedSingleton<IGrainStorage>(DigitalBrainNames.DefaultGrainStorage, (services, _) =>
                new LegacyStorage(new OrleansGrainStorageSerializer(services.GetRequiredService<Serializer>())));
        }).StartAsync(ct);
        CallerContextStamper.Stamp(new() { PrincipalId = "alice", AccountId = "alice", BrainId = "brain", AppId = "app", Kind = CallerKind.App, StampedBy = TrustedEdge.AppProxy });
        var table = brain.Get<IPostgresTable>("legacy");
        var definition = new TableDefinition([new("id", "text")], ["id"]);
        Assert.Equal("legacy_table", (await table.Define(definition)).Table);
        await brain.DeactivateAsync(table, ct);
        Assert.Equal("legacy_table", (await table.Define(definition)).Table);
        var storage = (LegacyStorage)brain.SiloServices.GetRequiredKeyedService<IGrainStorage>(DigitalBrainNames.DefaultGrainStorage);
        storage.FailClear = true;
        var app = brain.Get<IPostgresLifecycleTestApp>("legacy-install");
        await Assert.ThrowsAnyAsync<Exception>(() => app.Uninstall("brain", "app"));
        await brain.DeactivateAsync(table, ct);
        await app.Uninstall("brain", "app");
        Assert.Equal(2, provider.Drops.Count);
        Assert.All(provider.Drops, drop => Assert.Equal(("platform", "legacy_table"), drop));
        Assert.Null(storage.Last!.Owner);
        Assert.Null(storage.Last.Accepted);
        Assert.Null(storage.Last.Origin);
        await brain.DeactivateAsync(table, ct);
        await app.Uninstall("brain", "app");
        Assert.Equal(2, provider.Drops.Count);
    }

    private sealed class LegacyStorage(IGrainStorageSerializer serializer) : IGrainStorage
    {
        private readonly Dictionary<(string, GrainId), BinaryData> written = [];
        public bool FailClear { get; set; }
        public PostgresTableState? Last { get; private set; }
        public async Task ReadStateAsync<T>(string name, GrainId id, IGrainState<T> state)
        {
            if (!written.TryGetValue((name, id), out var bytes))
            {
                if (typeof(T) != typeof(PostgresTableState)) { return; }
                bytes = new(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "LegacyState", "table.orleans"), TestContext.Current.CancellationToken));
            }
            state.State = serializer.Deserialize<T>(bytes);
            state.RecordExists = true;
            state.ETag = "read";
        }
        public Task WriteStateAsync<T>(string name, GrainId id, IGrainState<T> state)
        {
            if (state.State is PostgresTableState table)
            {
                if (FailClear && table.Owner is null) { FailClear = false; throw new IOException("Lost the save after DROP."); }
                Last = table;
            }
            written[(name, id)] = serializer.Serialize(state.State);
            state.RecordExists = true;
            state.ETag = "written";
            return Task.CompletedTask;
        }
        public Task ClearStateAsync<T>(string name, GrainId id, IGrainState<T> state) => throw new NotSupportedException();
    }
}
