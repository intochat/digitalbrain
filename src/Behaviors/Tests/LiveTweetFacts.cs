using DigitalBrain.Behaviors;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class LiveTweetFacts
{
    [Fact]
    public async Task LateObserversReceiveOnlyNewTweets()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.StartAsync(new() { Modules = [new DigitalBrain.Core.ModuleDefinition(typeof(TestTwitterModule))] }, ct);
        var twitter = brain.Get<ITwitterAccount>("late");
        await twitter.Post("old");
        await using var received = await brain.Observe<Posted>(twitter, ct);
        await twitter.Post("new");
        Assert.Equal("new", (await received.NextAsync(ct: ct)).Text);
    }
}
