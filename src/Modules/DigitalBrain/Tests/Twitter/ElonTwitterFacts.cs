using System.Net.Http.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ElonTwitterFacts
{
    [Fact]
    public async Task GenericBroadcastReachesTheBrainFromEveryTwitterAccount()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync(new()
        {
            Modules = [new TestTwitterModule()],
        });

        await brain.Get<ITwitterAccount>("elonmusk").Post("launching");
        await brain.Get<ITwitterAccount>("jack").Post("hello");

        var posts = await WaitUntil(
            () => brain.Signals(),
            signals => signals.OfType<Posted>().Select(post => post.From).Distinct().Count() >= 2,
            cancellation);

        Assert.Contains(posts.OfType<Posted>(), post => post is { From: "elonmusk", Text: "launching" });
        Assert.Contains(posts.OfType<Posted>(), post => post is { From: "jack", Text: "hello" });
    }

    [Fact]
    public async Task ElonBitcoinBehaviorQuotesOnUiWhenThePostContainsBitcoin()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync(new()
        {
            Modules = [new TestTwitterModule()],
        });

        await brain.Get<IBitcoin>("btc").SetPrice(97_400m);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var behavior = ElonBitcoin.Run(brain, run.Token);

        (await brain.Http.PostAsJsonAsync("/twitter/webhook", new TweetWebhook("jack", "buy Bitcoin"), cancellation))
            .EnsureSuccessStatusCode();
        (await brain.Http.PostAsJsonAsync("/twitter/webhook", new TweetWebhook("elonmusk", "to the moon"), cancellation))
            .EnsureSuccessStatusCode();

        var notes = await WaitUntil(
            async () =>
            {
                (await brain.Http.PostAsJsonAsync(
                    "/twitter/webhook",
                    new TweetWebhook("elonmusk", "buy Bitcoin"),
                    cancellation)).EnsureSuccessStatusCode();
                return await brain.Get<INotification>("ui").Read();
            },
            sent => sent.Count > 0,
            cancellation);
        Assert.All(notes, note => Assert.Equal("elonmusk: buy Bitcoin  BTC 97400", note));

        await run.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => behavior);
    }

    private static async Task<T> WaitUntil<T>(Func<Task<T>> read, Func<T, bool> done, CancellationToken cancellation)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        T last = default!;
        while (DateTime.UtcNow < deadline)
        {
            last = await read();
            if (done(last))
            {
                return last;
            }

            await Task.Delay(20, cancellation);
        }

        throw new TimeoutException($"Condition not met. Last: {last}");
    }
}
