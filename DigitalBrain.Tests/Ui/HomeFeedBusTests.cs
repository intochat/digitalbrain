using DigitalBrain.Core;
using DigitalBrain.Core.Ui;
using DigitalBrain.Kernel.Ui;

namespace DigitalBrain.Tests.Ui;

public class HomeFeedBusTests
{
    [Fact]
    public async Task Broadcast_Reaches_Subscribers()
    {
        var bus = new HomeFeedBus();
        using var subscription = bus.Subscribe();

        bus.Broadcast(new RfwCard("digitalbrain", "Card", "{\"a\":1}"));

        var received = await subscription.Reader.ReadAsync();
        Assert.Equal("Card", received.RootWidget);
    }

    [Fact]
    public async Task Identical_Cards_Are_Deduped()
    {
        var bus = new HomeFeedBus();
        using var subscription = bus.Subscribe();

        var card = new RfwCard("digitalbrain", "Card", "{\"a\":1}");
        bus.Broadcast(card);
        bus.Broadcast(card); // identical content -> deduped

        Assert.True(subscription.Reader.TryRead(out var first));
        Assert.Equal("Card", first!.RootWidget);
        Assert.False(subscription.Reader.TryRead(out _)); // nothing more
    }

    [Fact]
    public async Task Subscriber_With_SessionId_Only_Receives_Cards_Addressed_To_It_Or_Unaddressed()
    {
        var bus = new HomeFeedBus();
        using var subscriptionA = bus.Subscribe("session-a");

        bus.Broadcast(new RfwCard("digitalbrain", "ForA", "{}", "session-a"));
        bus.Broadcast(new RfwCard("digitalbrain", "ForB", "{}", "session-b"));
        bus.Broadcast(new RfwCard("digitalbrain", "Unaddressed", "{}"));

        var first = await subscriptionA.Reader.ReadAsync();
        var second = await subscriptionA.Reader.ReadAsync();
        Assert.Equal("ForA", first.RootWidget);
        Assert.Equal("Unaddressed", second.RootWidget);
        Assert.False(subscriptionA.Reader.TryRead(out _));
    }

    // TEMPORARY: fails open until the client can register a real session (see HomeFeedBus.FanLocal's
    // comment) — a subscriber that never registers a session sees every card, addressed or not, matching
    // pre-existing (pre-multiuser) behavior. This is intentionally the opposite of the stricter isolation
    // this bus is designed to enforce once a real per-subscriber session is available.
    [Fact]
    public async Task Subscriber_Without_SessionId_Receives_Every_Card_Fail_Open()
    {
        var bus = new HomeFeedBus();
        using var subscription = bus.Subscribe();

        bus.Broadcast(new RfwCard("digitalbrain", "ForA", "{}", "session-a"));
        bus.Broadcast(new RfwCard("digitalbrain", "Unaddressed", "{}"));

        var first = await subscription.Reader.ReadAsync();
        var second = await subscription.Reader.ReadAsync();
        Assert.Equal("ForA", first.RootWidget);
        Assert.Equal("Unaddressed", second.RootWidget);
        Assert.False(subscription.Reader.TryRead(out _));
    }
}

