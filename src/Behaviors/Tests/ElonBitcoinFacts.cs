using DigitalBrain.Testing.Unit;
using DigitalBrain.Behaviors;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Inbox;
using DigitalBrain.Flutter.Inbox.Signals;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ElonBitcoinFacts
{
    [Fact]
    public async Task BitcoinPostShowsOnTheUiInbox()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<TestTwitterModule>().WithModule<FlutterModule>()
            .StartAsync(ct);
        await brain.Get<IBitcoin>("btc").SetPrice(64_000);
        await using var run = brain.RunBehavior((live, token) => new ElonBitcoin(live).RunAsync(token), ct);
        var elon = brain.Get<ITwitterAccount>("elonmusk");
        var ui = brain.Get<IInbox>("ui");
        await using var inbox = await brain.Observe<InboxAppeared>(ui, ct);
        await run.WaitForSubscriptionAsync<Posted>(elon, ct);
        await elon.Post("hello");
        await elon.Post("Bitcoin to the moon");
        var appeared = await inbox.NextAsync(ct: ct);
        Assert.Equal("elonmusk: Bitcoin to the moon  BTC 64000", appeared.Text);
        Assert.Single(inbox.Snapshot);
    }
}
