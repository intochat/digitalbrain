using System.Text.Json.Serialization;
using Azure.Storage.Blobs;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Journaling.Json;
using Orleans.TestingHost;
using Testcontainers.Azurite;
using Xunit;

namespace Brain.FeasibilityTests.Journaling;

public sealed class OfficialJournalRecoveryTests : IAsyncLifetime
{
    private readonly AzuriteContainer _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:latest")
        .WithCommand("--skipApiVersionCheck")
        .Build();

    private string _connectionString = null!;
    private string _containerName = null!;

    public async Task InitializeAsync()
    {
        await _azurite.StartAsync();
        _connectionString = _azurite.GetConnectionString();
        _containerName = "journals-" + Guid.NewGuid().ToString("N");
        await new BlobServiceClient(_connectionString).CreateBlobContainerAsync(_containerName);
    }

    public async Task DisposeAsync()
    {
        await _azurite.DisposeAsync();
    }

    [Fact]
    public async Task Complete_restart_recovers_all_four_durable_structures_and_continues()
    {
        var grainKey = "recovery-" + Guid.NewGuid().ToString("N");
        var mapKey1 = Guid.NewGuid();
        var queueItem1 = Guid.NewGuid();
        var mapKey2 = Guid.NewGuid();
        var queueItem2 = Guid.NewGuid();

        await using (var firstCluster = await CreateClusterAsync())
        {
            var grain = firstCluster.GrainFactory.GetGrain<IJournalRecoveryGrain>(grainKey);
            await grain.WriteAllAsync(1, mapKey1, "value-1", queueItem1, "list-1");
            await firstCluster.StopAllSilosAsync();
        }

        await using (var secondCluster = await CreateClusterAsync())
        {
            var grain = secondCluster.GrainFactory.GetGrain<IJournalRecoveryGrain>(grainKey);
            var recovered = await grain.ReadAllAsync();
            Assert.Equal(1, recovered.Counter);
            Assert.Equal("value-1", recovered.Map[mapKey1]);
            Assert.Equal([queueItem1], recovered.Queue);
            Assert.Equal(["list-1"], recovered.List);

            await grain.WriteAllAsync(2, mapKey2, "value-2", queueItem2, "list-2");
            await secondCluster.StopAllSilosAsync();
        }

        await using (var thirdCluster = await CreateClusterAsync())
        {
            var grain = thirdCluster.GrainFactory.GetGrain<IJournalRecoveryGrain>(grainKey);
            var continued = await grain.ReadAllAsync();
            Assert.Equal(2, continued.Counter);
            Assert.Equal("value-1", continued.Map[mapKey1]);
            Assert.Equal("value-2", continued.Map[mapKey2]);
            Assert.Equal([queueItem1, queueItem2], continued.Queue);
            Assert.Equal(["list-1", "list-2"], continued.List);
            await thirdCluster.StopAllSilosAsync();
        }
    }

    [Fact]
    public async Task Failed_journal_write_prevents_external_effect_probe()
    {
        var grainKey = "failed-intent-" + Guid.NewGuid().ToString("N");

        await using var cluster = await CreateClusterAsync();
        var grain = cluster.GrainFactory.GetGrain<IJournalRecoveryGrain>(grainKey);
        await grain.WriteAllAsync(1, Guid.NewGuid(), "seed", Guid.NewGuid(), "seed-list");

        JournalRecoveryExternalEffectProbe.Reset();
        await _azurite.StopAsync();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await grain.CommitIntentThenExternalEffectAsync(99));

        Assert.Equal(0, JournalRecoveryExternalEffectProbe.Count);
        await cluster.StopAllSilosAsync();
    }

    private async Task<TestCluster> CreateClusterAsync()
    {
        OfficialJournalSiloConfigurator.ConnectionString = _connectionString;
        OfficialJournalSiloConfigurator.ContainerName = _containerName;

        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<OfficialJournalSiloConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }

    private sealed class OfficialJournalSiloConfigurator : ISiloConfigurator
    {
        public static string ConnectionString { get; set; } = "";
        public static string ContainerName { get; set; } = "";

        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.AddJournalStorage();
            siloBuilder.UseJsonJournalFormat(JournalRecoveryJsonContext.Default);
            siloBuilder.AddAzureBlobJournalStorage(options =>
            {
                options.ConfigureBlobServiceClient(ConnectionString);
                options.ContainerName = ContainerName;
            });
        }
    }
}

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(byte))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
internal sealed partial class JournalRecoveryJsonContext : JsonSerializerContext;
