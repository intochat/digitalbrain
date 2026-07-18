using Ino.Core.Hosting;
using Xunit;

namespace Ino.Core.Hosting.Tests;

public class InMemoryInoEventBusTests
{
    static InoEvent Evt(string type, string correlationId = "c1") =>
        new(type, SourceNeuron: "gateway", CorrelationId: correlationId,
            Payload: ReadOnlyMemory<byte>.Empty, TimestampUnixMs: 0);

    [Fact]
    public async Task Subscribe_receives_events_published_after_subscription_starts()
    {
        var bus = new InMemoryInoEventBus();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var task = Task.Run(async () =>
        {
            var received = new List<InoEvent>();
            try
            {
                await foreach (var evt in bus.SubscribeAsync("u1", cts.Token))
                {
                    received.Add(evt);
                    if (received.Count == 2) break;
                }
            }
            catch (OperationCanceledException) { }
            return received;
        }, cts.Token);

        // Give the consumer a tick to register its channel before publishing.
        await Task.Delay(50, TestContext.Current.CancellationToken);
        bus.Publish("u1", Evt("synapse.fire"));
        bus.Publish("u1", Evt("synapse.broadcast"));

        var received = await task;
        Assert.Equal(2, received.Count);
        Assert.Equal(new[] { "synapse.fire", "synapse.broadcast" }, received.Select(e => e.Type));
    }

    [Fact]
    public async Task Events_for_other_users_are_not_delivered()
    {
        var bus = new InMemoryInoEventBus();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        var task = Task.Run(async () =>
        {
            var received = new List<InoEvent>();
            try
            {
                await foreach (var evt in bus.SubscribeAsync("u1", cts.Token))
                {
                    received.Add(evt);
                }
            }
            catch (OperationCanceledException) { }
            return received;
        }, cts.Token);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        bus.Publish("u2", Evt("synapse.fire", "other"));
        bus.Publish("u1", Evt("synapse.fire", "mine"));

        var received = await task;
        var only = Assert.Single(received);
        Assert.Equal("mine", only.CorrelationId);
    }

    [Fact]
    public async Task Multiple_subscriptions_for_same_user_all_receive_the_event()
    {
        var bus = new InMemoryInoEventBus();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        async Task<int> Collect()
        {
            var count = 0;
            try
            {
                await foreach (var _ in bus.SubscribeAsync("u1", cts.Token))
                {
                    count++;
                    if (count == 1) break;
                }
            }
            catch (OperationCanceledException) { }
            return count;
        }

        var a = Task.Run(Collect);
        var b = Task.Run(Collect);

        await Task.Delay(100, TestContext.Current.CancellationToken);
        bus.Publish("u1", Evt("synapse.fire"));

        var results = await Task.WhenAll(a, b);
        Assert.All(results, count => Assert.Equal(1, count));
    }

    [Fact]
    public void Publish_with_no_subscribers_is_a_no_op()
    {
        var bus = new InMemoryInoEventBus();
        var act = () => bus.Publish("nobody", Evt("synapse.fire"));
        var ex = Record.Exception(act);
        Assert.Null(ex);
    }
}
