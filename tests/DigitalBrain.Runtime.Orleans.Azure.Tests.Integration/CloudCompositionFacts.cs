using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Orleans.Streams;

namespace DigitalBrain.Runtime.Orleans.Azure.Tests.Integration;

public sealed class AzuriteFixtureFacts
{
    [Theory]
    [InlineData("UseDevelopmentStorage=true", true, false, AzuriteMode.UseConfigured)]
    [InlineData("UseDevelopmentStorage=true", false, true, AzuriteMode.UseConfigured)]
    [InlineData(null, false, true, AzuriteMode.StartContainer)]
    [InlineData(null, true, true, AzuriteMode.StartContainer)]
    [InlineData(null, false, false, AzuriteMode.Skip)]
    [InlineData("", true, false, AzuriteMode.Fail)]
    [InlineData(null, true, false, AzuriteMode.Fail)]
    public void In_CI_a_missing_Azurite_fails_and_never_skips(string? configured, bool ci, bool docker, AzuriteMode expected)
        => Assert.Equal(expected, AzuriteFixture.Decide(configured, ci, docker));

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public async Task Process_output_keeps_pull_progress_out_of_successful_container_ids(int exitCode)
    {
        using var home = new TempHome();
        Directory.CreateDirectory(home.Path);
        var windows = OperatingSystem.IsWindows();
        var script = Path.Combine(home.Path, windows ? "output.cmd" : "output.sh");
        await File.WriteAllTextAsync(script, windows
            ? $"@echo off\r\necho container-id\r\necho image-pull-status 1>&2\r\nexit /b {exitCode}\r\n"
            : $"printf 'container-id\\n'\nprintf 'image-pull-status\\n' >&2\nexit {exitCode}\n", TestContext.Current.CancellationToken);

        var (code, output) = await AzuriteFixture.Run(windows ? "cmd.exe" : "/bin/sh",
            windows ? $"/d /c \"{script}\"" : $"\"{script}\"", TimeSpan.FromSeconds(10));

        Assert.Equal(exitCode, code);
        if (exitCode == 0) { Assert.Equal("container-id", output.Trim()); }
        else
        {
            Assert.Contains("container-id", output, StringComparison.Ordinal);
            Assert.Contains("image-pull-status", output, StringComparison.Ordinal);
        }
    }
}

public sealed class CloudCompositionFacts(AzuriteFixture azurite)
{
    [Fact]
    public async Task A_cloud_silo_with_the_host_cluster_store_round_trips_platform_state_through_blob_storage_across_restarts()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = azurite.Require();
        var clusterId = "c" + Guid.NewGuid().ToString("N")[..12];
        using var home = new TempHome();
        var fake = new FakeClusterStore();

        using (var host = CloudHosts.Build(storage, home.Path, fake, clusterId, typeof(DriveModule)))
        {
            var services = host.Services;
            Assert.Same(fake, services.GetRequiredKeyedService<IGrainStorage>(IntoChatOSNames.ClusterGrainStorage));
            Assert.IsType<AzureBlobGrainStorage>(services.GetRequiredKeyedService<IGrainStorage>(IntoChatOSNames.PlatformGrainStorage));
            Assert.Equal(CloudHosts.MasterKey, services.GetRequiredService<IConfiguration>()[IntoChatOSNames.MasterKeyKey]);

            await host.StartAsync(ct);
            await services.GetRequiredService<IGrainFactory>().GetGrain<ICloudProbeGrain>("p1").Set(42);

            // Table clustering: this silo is an active member in the Azure table.
            var hosts = await services.GetRequiredService<IGrainFactory>().GetGrain<IManagementGrain>(0).GetHosts(true, ct);
            Assert.Single(hosts);
            var members = new TableServiceClient(storage).GetTableClient("OrleansSiloInstances")
                .QueryAsync<TableEntity>(e => e.PartitionKey == clusterId, cancellationToken: ct);
            var rows = new List<TableEntity>();
            await foreach (var row in members) { rows.Add(row); }
            Assert.Contains(rows, row => row.GetString("Status") == "Active");

            // Queue streams: the signals provider writes to signals-0.
            var stream = services.GetRequiredKeyedService<IStreamProvider>(IntoChatOSNames.SignalStreams)
                .GetStream<int>(StreamId.Create(IntoChatOSNames.SignalStreams, "probe"));
            await stream.OnNextAsync(1);
            Assert.True((await new QueueServiceClient(storage).GetQueueClient("signals-0").ExistsAsync(ct)).Value);

            await host.StopAsync(ct);
        }

        var blobs = new BlobServiceClient(storage).GetBlobContainerClient(IntoChatOSNames.GrainStateContainer);
        Assert.True((await blobs.ExistsAsync(ct)).Value);

        using (var host = CloudHosts.Build(storage, home.Path, fake, clusterId, typeof(DriveModule)))
        {
            await host.StartAsync(ct);
            Assert.Equal(42, await host.Services.GetRequiredService<IGrainFactory>().GetGrain<ICloudProbeGrain>("p1").Get());
            await host.StopAsync(ct);
        }
        Assert.Equal(2, fake.Registered);
    }
}
