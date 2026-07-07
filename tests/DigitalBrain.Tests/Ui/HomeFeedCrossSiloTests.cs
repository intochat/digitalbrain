using DigitalBrain.Core;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.TestKit;
using DigitalBrain.Ui.Contracts.Ui;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.Ui;

public class HomeFeedCrossSiloTests : NeuronTestBase
{
    protected override short InitialSilosCount => 2;

    [Fact]
    public async Task Broadcast_On_Silo0_Received_On_Silo1_Unaddressed()
    {
        var bus0 = ((InProcessSiloHandle)Cluster.Silos[0]).SiloHost.Services.GetRequiredService<HomeFeedBus>();
        var bus1 = ((InProcessSiloHandle)Cluster.Silos[1]).SiloHost.Services.GetRequiredService<HomeFeedBus>();

        await using var subscription = await bus1.SubscribeAsync(clientId: null);
        var card = new RfwCard("digitalbrain", "CrossSiloCard", "{\"x\":1}");

        await bus0.BroadcastAsync(card);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = await subscription.Reader.ReadAsync(cts.Token);
        Assert.Equal("CrossSiloCard", received.RootWidget);
    }

    [Fact]
    public async Task Broadcast_Addressed_To_ClientId_Received_Only_By_That_Clients_Silo()
    {
        var bus0 = ((InProcessSiloHandle)Cluster.Silos[0]).SiloHost.Services.GetRequiredService<HomeFeedBus>();
        var bus1 = ((InProcessSiloHandle)Cluster.Silos[1]).SiloHost.Services.GetRequiredService<HomeFeedBus>();

        // Silo 1 holds the subscriber for "client-a"; silo 0 publishes the card that's addressed to it.
        await using var subscriptionA = await bus1.SubscribeAsync(clientId: "client-a");
        var card = new RfwCard("digitalbrain", "AddressedCrossSiloCard", "{\"x\":2}", ClientId: "client-a");

        await bus0.BroadcastAsync(card);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = await subscriptionA.Reader.ReadAsync(cts.Token);
        Assert.Equal("AddressedCrossSiloCard", received.RootWidget);
    }

    [Fact]
    public async Task Broadcast_On_Silo0_Also_Delivered_To_Silo0_Subscriber()
    {
        // In cluster mode Broadcast goes out via the stream and loops back through this silo's own subscriber,
        // so a client connected to the producing silo must still receive the card (no synchronous local fanout).
        var bus0 = ((InProcessSiloHandle)Cluster.Silos[0]).SiloHost.Services.GetRequiredService<HomeFeedBus>();

        await using var subscription = await bus0.SubscribeAsync(clientId: null);
        var card = new RfwCard("digitalbrain", "SelfDeliveryCard", "{\"x\":3}");

        await bus0.BroadcastAsync(card);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = await subscription.Reader.ReadAsync(cts.Token);
        Assert.Equal("SelfDeliveryCard", received.RootWidget);
    }

    [Fact]
    public async Task Card_Addressed_To_One_ClientId_Never_Reaches_A_Different_Clients_Subscriber()
    {
        var bus0 = ((InProcessSiloHandle)Cluster.Silos[0]).SiloHost.Services.GetRequiredService<HomeFeedBus>();

        await using var subscriptionB = await bus0.SubscribeAsync(clientId: "client-b");
        await bus0.BroadcastAsync(new RfwCard("digitalbrain", "AddressedToA", "{}", ClientId: "client-a"));
        await bus0.BroadcastAsync(new RfwCard("digitalbrain", "UnaddressedSystemCard", "{}"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        // Only the unaddressed card should ever arrive on B's subscription — read exactly one message and
        // confirm it's the system card, then confirm nothing else shows up within a bounded window.
        var first = await subscriptionB.Reader.ReadAsync(cts.Token);
        Assert.Equal("UnaddressedSystemCard", first.RootWidget);

        using var drainCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => subscriptionB.Reader.ReadAsync(drainCts.Token).AsTask());
    }
}
