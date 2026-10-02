using Azure.Storage.Blobs;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Storage;

namespace DigitalBrain.Postgres;

[PlatformOnly]
internal interface IPostgresTablesImport : INeuron
{
    Task Import(string table);
}

[GenerateSerializer]
internal sealed record PostgresMigrationState
{
    [Id(0)] public bool Complete { get; init; }
}

[GrainType("postgres.migration")]
internal sealed class PostgresStorageMigration(IPostgresLegacyTables legacy,
    [PersistentState("migration", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<PostgresMigrationState> state)
    : Neuron, IPostgresStorageMigration
{
    public async Task Ensure()
    {
        if (state.State.Complete) { return; }
        foreach (var table in await legacy.Read(CancellationToken.None))
        { await GrainFactory.GetGrain<IPostgresTablesImport>(table.Owner).Import(table.Id); }
        state.State = new() { Complete = true };
        try { await state.WriteStateAsync(); }
        catch { state.State = new(); throw; }
    }
}

internal sealed class PostgresLegacyTables(IServiceProvider services) : IPostgresLegacyTables
{
    public async Task<LegacyPostgresTable[]> Read(CancellationToken ct)
    {
        var blobs = services.GetKeyedService<BlobServiceClient>(DigitalBrainNames.GrainState);
        if (blobs is null) { return []; }
        var serializer = services.GetKeyedService<IGrainStorageSerializer>(DigitalBrainNames.DefaultGrainStorage)
            ?? new OrleansGrainStorageSerializer(services.GetRequiredService<Serializer>());
        var container = blobs.GetBlobContainerClient("digitalbrain-v2-state");
        if (!(await container.ExistsAsync(ct)).Value) { return []; }
        var result = new List<LegacyPostgresTable>();
        // Orleans AzureBlobGrainStorage names blobs "{stateName}-{grainId}.json", even for binary state.
        const string prefix = "state-postgres.table/";
        await foreach (var blob in container.GetBlobsAsync(Azure.Storage.Blobs.Models.BlobTraits.None, Azure.Storage.Blobs.Models.BlobStates.None, prefix, ct))
        {
            if (!blob.Name.EndsWith(".json", StringComparison.Ordinal)) { throw new InvalidDataException("Unrecognized Postgres state blob."); }
            var bytes = (await container.GetBlobClient(blob.Name).DownloadContentAsync(ct)).Value.Content;
            var table = serializer.Deserialize<PostgresTableState>(bytes) ?? throw new InvalidDataException("Unreadable Postgres table state.");
            if (table.Owner is { } owner)
            { result.Add(new(blob.Name[prefix.Length..^5], owner)); }
        }
        return result.ToArray();
    }
}

internal sealed class PostgresMigrationStartup(IGrainFactory grains) : IStartupTask
{
    public async Task Execute(CancellationToken cancellationToken)
    {
        CallerContextStamper.TryGet(out var previous);
        try
        {
            CallerContextStamper.Stamp(new() { PrincipalId = "platform", AccountId = "platform", BrainId = "migration", Kind = CallerKind.Platform, StampedBy = TrustedEdge.Platform });
            await grains.GetGrain<IPostgresStorageMigration>("table-owners-v1").Ensure().WaitAsync(cancellationToken);
        }
        finally
        {
            if (previous is null) { RequestContext.Remove(CallerContextStamper.RequestContextKey); }
            else { CallerContextStamper.Stamp(previous); }
        }
    }
}
