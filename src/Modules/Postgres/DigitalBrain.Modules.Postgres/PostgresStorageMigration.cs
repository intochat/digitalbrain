using Azure.Storage.Blobs;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
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
    [Id(1)] public string[] SkippedBlobs { get; init; } = [];
}

[GrainType("postgres.migration")]
internal sealed class PostgresStorageMigration(IPostgresLegacyTables legacy,
    [PersistentState("migration", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<PostgresMigrationState> state)
    : Neuron, IPostgresStorageMigration
{
    public async Task Ensure()
    {
        if (state.State.Complete) { return; }
        var scan = await legacy.Read(CancellationToken.None);
        foreach (var table in scan.Tables)
        { await GrainFactory.GetGrain<IPostgresTablesImport>(table.Owner).Import(table.Id); }
        state.State = new() { Complete = true, SkippedBlobs = scan.SkippedBlobs };
        try { await state.WriteStateAsync(); }
        catch { state.State = new(); throw; }
    }

    public Task<string[]> ReadSkippedBlobs() => Task.FromResult(state.State.SkippedBlobs.ToArray());
}

internal sealed class PostgresLegacyTables(IServiceProvider services) : IPostgresLegacyTables
{
    public async Task<LegacyPostgresScan> Read(CancellationToken ct)
    {
        var blobs = services.GetKeyedService<BlobServiceClient>(DigitalBrainNames.GrainState);
        if (blobs is null) { return new([], []); }
        var serializer = services.GetKeyedService<IGrainStorageSerializer>(DigitalBrainNames.DefaultGrainStorage)
            ?? new OrleansGrainStorageSerializer(services.GetRequiredService<Serializer>());
        var options = services.GetRequiredService<IOptionsMonitor<AzureBlobStorageOptions>>().Get(DigitalBrainNames.DefaultGrainStorage);
        var container = blobs.GetBlobContainerClient(options.ContainerName);
        if (!(await container.ExistsAsync(ct)).Value) { return new([], []); }
        var result = new List<LegacyPostgresTable>();
        var skipped = new List<string>();
        var parsed = 0;
        var logger = services.GetRequiredService<ILogger<PostgresLegacyTables>>();
        // Deliberately coupled to AzureBlobGrainStorage's "{stateName}-{grainId}.json" naming,
        // even for binary state. A storage-provider change must update this migration connector.
        const string prefix = "state-postgres.table/";
        await foreach (var blob in container.GetBlobsAsync(Azure.Storage.Blobs.Models.BlobTraits.None, Azure.Storage.Blobs.Models.BlobStates.None, prefix, ct))
        {
            if (!blob.Name.EndsWith(".json", StringComparison.Ordinal))
            {
                skipped.Add(blob.Name);
                logger.LogWarning("Skipping unrecognized Postgres state blob {BlobName}", blob.Name);
                continue;
            }
            // Transport/authentication failures still abort; only individual invalid records are skipped.
            var bytes = (await container.GetBlobClient(blob.Name).DownloadContentAsync(ct)).Value.Content;
            PostgresTableState table;
            try { table = serializer.Deserialize<PostgresTableState>(bytes) ?? throw new InvalidDataException("Unreadable Postgres table state."); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                skipped.Add(blob.Name);
                logger.LogWarning(error, "Skipping unreadable Postgres state blob {BlobName}", blob.Name);
                continue;
            }
            parsed++;
            if (table.Owner is { } owner)
            { result.Add(new(blob.Name[prefix.Length..^5], owner)); }
        }
        if (parsed == 0 && skipped.Count > 0)
        { throw new InvalidDataException($"No Postgres state blobs could be read: {string.Join(", ", skipped)}."); }
        return new(result.ToArray(), skipped.ToArray());
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
