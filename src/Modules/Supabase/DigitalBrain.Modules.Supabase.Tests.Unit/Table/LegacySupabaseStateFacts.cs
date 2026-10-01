using DigitalBrain.Contracts;
using DigitalBrain.Supabase;
using DigitalBrain.Supabase.Tables;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Storage;
using Xunit;

namespace DigitalBrain.Modules.Supabase.Tests.Unit.Table;

public sealed class LegacySupabaseStateFacts
{
    [Fact]
    public async Task TableStateWrittenBeforeTheAssemblyMoveLoadsAndSurvivesReactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new FakeSupabaseProvider();
        await using var brain = await UnitTest.Create().WithModule<SupabaseModule>().ConfigureSilo(silo =>
        {
            silo.Services.AddSingleton<ISupabaseProvider>(provider);
            // The production Azure Blob provider uses the same Orleans storage serializer.
            silo.Services.AddKeyedSingleton<IGrainStorage>(DigitalBrainNames.DefaultGrainStorage, (services, _) =>
                new LegacyStorage(new OrleansGrainStorageSerializer(services.GetRequiredService<Serializer>())));
        }).StartAsync(ct);
        var table = brain.Get<ISupabaseTable>("legacy-table");
        var request = new CreateQueryTable("Legacy people", "select id from people");
        var saved = await table.CreateFromQueryOnce("legacy-operation", request, ct);
        Assert.Equal("legacy-table", saved.Id);
        Assert.Equal("Legacy people", saved.Title);
        Assert.Equal(1, saved.Revision);
        Assert.Equal("id", Assert.Single(saved.Columns).Id);
        Assert.Single((await table.Read(new(0, 50)))!.Rows);
        Assert.Equal("select id from people", provider.LastPlan!.BaseSql);

        var storage = Assert.IsType<LegacyStorage>(brain.SiloServices.GetRequiredKeyedService<IGrainStorage>(DigitalBrainNames.DefaultGrainStorage));
        Assert.Equal("legacy-operation", storage.Loaded!.CreationOperation);
        Assert.Equal(request, storage.Loaded.CreationRequest);
        Assert.Equal(new SupabaseColumn("id", "int4", SupabaseTypeMap.Number), Assert.Single(storage.Loaded.SourceColumns));
        var updated = await table.UpdateView(new(saved.Revision, [], null, ["id"]));
        await brain.DeactivateAsync(table, ct);
        var restored = await table.ReadSummary();
        Assert.Equal(updated.Revision, restored!.Revision);
        Assert.Equal(saved.Title, restored.Title);
        Assert.Equal(updated.Revision, (await table.CreateFromQueryOnce("legacy-operation", request, ct)).Revision);
        Assert.Single((await table.Read(new(0, 50)))!.Rows);
        Assert.True(storage.TableReads >= 2);
        Assert.True(storage.TableWrites >= 1);
        Assert.Equal("DigitalBrain.LiveTables", typeof(SupabaseTableState).Assembly.GetName().Name);
    }

    private sealed class LegacyStorage(IGrainStorageSerializer serializer) : IGrainStorage
    {
        private readonly Dictionary<(string, GrainId), BinaryData> _written = [];
        public SupabaseTableState? Loaded { get; private set; }
        public int TableReads { get; private set; }
        public int TableWrites { get; private set; }

        public async Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> state)
        {
            if (!_written.TryGetValue((stateName, grainId), out var data))
            {
                if (typeof(T) != typeof(SupabaseTableState)) { return; }
                data = new BinaryData(await File.ReadAllBytesAsync(
                    Path.Combine(AppContext.BaseDirectory, "Table", "LegacyState", "table.orleans"),
                    TestContext.Current.CancellationToken));
            }
            state.State = serializer.Deserialize<T>(data);
            state.RecordExists = true;
            state.ETag = "legacy";
            if (state.State is SupabaseTableState table)
            {
                Loaded = table;
                TableReads++;
            }
        }

        public Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> state)
        {
            _written[(stateName, grainId)] = serializer.Serialize(state.State);
            state.RecordExists = true;
            state.ETag = "updated";
            if (typeof(T) == typeof(SupabaseTableState)) { TableWrites++; }
            return Task.CompletedTask;
        }

        public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> state)
            => throw new NotSupportedException();
    }
}
