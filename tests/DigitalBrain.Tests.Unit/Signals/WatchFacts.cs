using System.Runtime.CompilerServices;
using DigitalBrain.Signals.Watch;

namespace DigitalBrain.Tests.Unit.Signals;

public sealed class WatchFacts
{
    public sealed record Changed(int Value) : Signal;

    // Serves each neuron its prepared signals after reporting the subscription, then keeps the stream open.
    private sealed class FakeWatch(Dictionary<string, int[]> signals) : ISignalWatch
    {
        public List<string> Subscribed { get; } = [];

        public async IAsyncEnumerable<T> On<T>(object neuron, Func<Task> onSubscribed, [EnumeratorCancellation] CancellationToken cancellationToken = default)
            where T : Signal
        {
            Subscribed.Add((string)neuron);
            await onSubscribed();
            foreach (var value in signals[(string)neuron]) { yield return (T)(Signal)new Changed(value); }
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    [Fact]
    public async Task AFixedSetYieldsEachMembersSignalOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var watch = new FakeWatch(new() { ["a"] = [1], ["b"] = [2] });
        var seen = new List<int>();
        await foreach (var signal in Sources.Stream<Changed>(watch, Sources.Of("a", "a", "b"), ct))
        {
            seen.Add(signal.Value);
            if (seen.Count == 2) { break; }
        }
        Assert.Equal([1, 2], seen.Order());
        Assert.Equal(["a", "b"], watch.Subscribed.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AReactionRebuildsOnceWhenRegisteredThenOncePerSignal()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var rebuilds = 0;
        await Sources.React<Changed>(new FakeWatch(new() { ["a"] = [1] }), Sources.Of("a"), _ =>
        {
            if (Interlocked.Increment(ref rebuilds) == 2) { cts.Cancel(); }
            return Task.CompletedTask;
        }, cts.Token);
        Assert.Equal(2, rebuilds);
    }

    [Fact]
    public async Task DisposingAStreamMidReadReleasesTheSubscriptionWithoutThrowing()
    {
        var ct = TestContext.Current.CancellationToken;
        var released = false;
        var stream = new WatchStream<int>(token => Task.FromResult<(IAsyncEnumerable<int>, IAsyncDisposable)>((Never(token), new Handle(() => released = true))),
            ct, ct, () => Task.CompletedTask);
        var enumerator = stream.GetAsyncEnumerator(ct);
        var read = enumerator.MoveNextAsync();
        await enumerator.DisposeAsync();
        Assert.False(await read);
        Assert.True(released);
    }

    [Fact]
    public void ASkippedSignalIsReportedAsSkipped()
    {
        var skipped = new Changed(1);
        var other = new Changed(1);
        SignalDeliveries.Skip(skipped);
        Assert.True(SignalDeliveries.WasSkipped(skipped));
        Assert.False(SignalDeliveries.WasSkipped(other));
    }

    private static async IAsyncEnumerable<int> Never([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        yield break;
    }

    private sealed class Handle(Action release) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { release(); return ValueTask.CompletedTask; }
    }
}
