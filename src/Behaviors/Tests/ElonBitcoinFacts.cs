using DigitalBrain.Behaviors;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ElonBitcoinFacts
{
    [Fact]
    public async Task BitcoinPostShowsOnTheUiNotification()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync(new() { Modules = [new TestTwitterModule()] }, ct);
        await brain.Get<IBitcoin>("btc").SetPrice(64_000);
        await using var run = brain.RunBehavior((live, token) => new ElonBitcoin(live).RunAsync(token), ct);
        var elon = brain.Get<ITwitterAccount>("elonmusk");
        await run.WaitForSubscriptionAsync<Posted>(elon, ct);
        await elon.Post("hello");
        await elon.Post("Bitcoin to the moon");
        var notes = await TestWait.UntilAsync(_ => brain.Get<INotification>("ui").Read(), sent => sent.Count == 1, TimeSpan.FromSeconds(5), ct);
        Assert.Equal("elonmusk: Bitcoin to the moon  BTC 64000", notes.Single());
    }
}
