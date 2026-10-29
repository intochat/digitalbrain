using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Providers.Streams.Common;
using Orleans.Storage;
using Orleans.Streams;

namespace DigitalBrain.Runtime.Orleans.Azure.Tests.Integration;

// RT-06, 04 D-09: the cloud stream and PubSubStore bindings Phase 3 ships (03-08), proven on Azurite.
public sealed class CloudSignalStreamsFacts(AzuriteFixture azurite)
{
    [Fact]
    public async Task A_cloud_silo_binds_queue_signal_streams_over_a_durable_PubSubStore_and_passes_the_durability_check()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = azurite.Require();
        using var home = new TempHome();
        using var host = CloudHosts.Build(storage, home.Path, new FakeClusterStore(), "s" + Guid.NewGuid().ToString("N")[..12]);
        var services = host.Services;

        Assert.IsType<AzureBlobGrainStorage>(services.GetRequiredKeyedService<IGrainStorage>(IntoChatOSNames.PubSubStore));
        Assert.IsType<PersistentStreamProvider>(services.GetRequiredKeyedService<IStreamProvider>(IntoChatOSNames.SignalStreams));
        Assert.Contains("AzureQueue", services.GetRequiredKeyedService<IQueueAdapterFactory>(IntoChatOSNames.SignalStreams).GetType().Name, StringComparison.Ordinal);
        Assert.Empty(TopologyValidator.ValidatePubSubStore(Topology.Cloud, services));

        await host.StartAsync(ct); // the startup PubSubStore check runs at ServiceLifecycleStage.First
        await host.StopAsync(ct);
    }
}
