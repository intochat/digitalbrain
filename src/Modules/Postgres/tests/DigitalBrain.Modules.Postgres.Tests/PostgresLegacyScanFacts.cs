using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Serialization;
using Orleans.Storage;

namespace DigitalBrain.Modules.Postgres.Tests;

public sealed class PostgresLegacyScanFacts
{
    [Fact]
    public async Task TheScanUsesTheConfiguredContainerAndReportsUnreadableBlobsAlongsideValidTables()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().StartAsync(ct);
        var serializer = new OrleansGrainStorageSerializer(brain.SiloServices.GetRequiredService<Serializer>());
        var blobs = new ScanBlobs(new()
        {
            ["state-postgres.table/valid.json"] = serializer.Serialize(new PostgresTableState { Owner = "owner" }),
            ["state-postgres.table/broken.json"] = BinaryData.FromString("broken"),
            ["state-postgres.table/foreign.txt"] = BinaryData.FromString("foreign")
        });
        await using var services = Services(blobs, serializer);

        var result = await new PostgresLegacyTables(services).Read(ct);

        Assert.Equal("custom-state", blobs.RequestedContainer);
        Assert.Equal("valid", Assert.Single(result.Tables).Id);
        Assert.Equal(["state-postgres.table/broken.json", "state-postgres.table/foreign.txt"], result.SkippedBlobs);
    }

    [Fact]
    public async Task AScanWithOnlyUnreadableBlobsRefusesCompletion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().StartAsync(ct);
        var serializer = new OrleansGrainStorageSerializer(brain.SiloServices.GetRequiredService<Serializer>());
        await using var services = Services(new ScanBlobs(new() { ["state-postgres.table/broken.json"] = BinaryData.FromString("broken") }), serializer);

        await Assert.ThrowsAsync<InvalidDataException>(() => new PostgresLegacyTables(services).Read(ct));
    }

    private static ServiceProvider Services(BlobServiceClient blobs, IGrainStorageSerializer serializer)
        => new ServiceCollection().AddLogging()
            .AddKeyedSingleton(DigitalBrainNames.GrainState, blobs)
            .AddKeyedSingleton(DigitalBrainNames.DefaultGrainStorage, serializer)
            .Configure<AzureBlobStorageOptions>(DigitalBrainNames.DefaultGrainStorage, options => options.ContainerName = "custom-state")
            .BuildServiceProvider();

    private sealed class ScanBlobs(Dictionary<string, BinaryData> contents) : BlobServiceClient
    {
        public string? RequestedContainer { get; private set; }
        public override BlobContainerClient GetBlobContainerClient(string blobContainerName)
        { RequestedContainer = blobContainerName; return new ScanContainer(contents, blobContainerName == "custom-state"); }
    }

    private sealed class ScanContainer(Dictionary<string, BinaryData> contents, bool exists) : BlobContainerClient
    {
        public override Task<Response<bool>> ExistsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Response.FromValue(exists, null!));
        public override AsyncPageable<BlobItem> GetBlobsAsync(BlobTraits traits = BlobTraits.None, BlobStates states = BlobStates.None, string? prefix = null, CancellationToken cancellationToken = default)
            => AsyncPageable<BlobItem>.FromPages([Page<BlobItem>.FromValues(contents.Keys.Select(name => BlobsModelFactory.BlobItem(name: name)).ToArray(), null, null!)]);
        public override BlobClient GetBlobClient(string blobName) => new ScanBlob(contents[blobName]);
    }

    private sealed class ScanBlob(BinaryData contents) : BlobClient
    {
        public override Task<Response<BlobDownloadResult>> DownloadContentAsync(CancellationToken cancellationToken)
            => Task.FromResult(Response.FromValue(BlobsModelFactory.BlobDownloadResult(contents), null!));
    }
}
