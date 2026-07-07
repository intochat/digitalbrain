using DigitalBrain.Core;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.TestKit;
using DigitalBrain.Ui.Contracts.Ui;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.Ui;

// Single-silo coverage of HomeFeedBus's dedup + addressing behavior; cross-silo delivery is covered by
// HomeFeedCrossSiloTests. HomeFeedBus now requires a real IClusterClient (no more parameterless/local-fanout
// constructor), so every test resolves the singleton via the running silo's own DI container instead of
// `new HomeFeedBus()`.
public class HomeFeedBusTests : NeuronTestBase
{
    private HomeFeedBus Bus =>
        ((InProcessSiloHandle)Cluster.Silos[0]).SiloHost.Services.GetRequiredService<HomeFeedBus>();

    [Fact]
    public async Task Broadcast_Reaches_Subscribers()
    {
        await using var subscription = await Bus.SubscribeAsync(clientId: null);

        await Bus.BroadcastAsync(new RfwCard("digitalbrain", "Card", "{\"a\":1}"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = await subscription.Reader.ReadAsync(cts.Token);
        Assert.Equal("Card", received.RootWidget);
    }

    [Fact]
    public async Task Identical_Cards_Are_Deduped()
    {
        await using var subscription = await Bus.SubscribeAsync(clientId: null);

        var card = new RfwCard("digitalbrain", "Card", "{\"a\":1}");
        await Bus.BroadcastAsync(card);
        await Bus.BroadcastAsync(card); // identical content -> deduped

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var first = await subscription.Reader.ReadAsync(cts.Token);
        Assert.Equal("Card", first.RootWidget);

        using var drainCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => subscription.Reader.ReadAsync(drainCts.Token).AsTask());
    }

    [Fact]
    public async Task Subscriber_With_ClientId_Only_Receives_Cards_Addressed_To_It_Or_Unaddressed()
    {
        await using var subscriptionA = await Bus.SubscribeAsync(clientId: "client-a");

        await Bus.BroadcastAsync(new RfwCard("digitalbrain", "ForA", "{}", "client-a"));
        await Bus.BroadcastAsync(new RfwCard("digitalbrain", "ForB", "{}", "client-b"));
        await Bus.BroadcastAsync(new RfwCard("digitalbrain", "Unaddressed", "{}"));

        // "ForA" arrives via the personal-stream subscription and "Unaddressed" via the shared-stream
        // subscription — two independent async pipelines with no guaranteed relative order, so collect both
        // and assert membership rather than a specific arrival order.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var first = await subscriptionA.Reader.ReadAsync(cts.Token);
        var second = await subscriptionA.Reader.ReadAsync(cts.Token);
        Assert.Equal(new[] { "ForA", "Unaddressed" }, new[] { first.RootWidget, second.RootWidget }.OrderBy(w => w));

        using var drainCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => subscriptionA.Reader.ReadAsync(drainCts.Token).AsTask());
    }

    [Fact]
    public async Task Subscriber_Without_ClientId_Never_Receives_Cards_Addressed_To_Someone_Else()
    {
        await using var subscription = await Bus.SubscribeAsync(clientId: null);

        await Bus.BroadcastAsync(new RfwCard("digitalbrain", "ForA", "{}", "client-a"));
        await Bus.BroadcastAsync(new RfwCard("digitalbrain", "Unaddressed", "{}"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var first = await subscription.Reader.ReadAsync(cts.Token);
        Assert.Equal("Unaddressed", first.RootWidget);

        using var drainCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => subscription.Reader.ReadAsync(drainCts.Token).AsTask());
    }
}
