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

    public sealed record RegistryChanged : Signal;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // One channel per neuron that the test publishes into. DeliveredAsync reports a publish as delivered once the
    // pump has moved it on, so a coalescing reader sees the whole burst queued.
    private sealed class ControlledWatch : ISignalWatch, ISignalDelivery
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.Channels.Channel<Signal>> channels = new();
        private int published;
        private int moved;

        private System.Threading.Channels.Channel<Signal> ChannelOf(string neuron)
            => channels.GetOrAdd(neuron, _ => System.Threading.Channels.Channel.CreateUnbounded<Signal>());

        public void Publish(string neuron, Signal signal)
        {
            Interlocked.Increment(ref published);
            ChannelOf(neuron).Writer.TryWrite(signal);
        }

        public async IAsyncEnumerable<T> On<T>(object neuron, Func<Task> onSubscribed, [EnumeratorCancellation] CancellationToken cancellationToken = default)
            where T : Signal
        {
            var channel = ChannelOf((string)neuron);
            await onSubscribed();
            await foreach (var signal in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return (T)signal;
                Interlocked.Increment(ref moved);
            }
        }

        public async Task DeliveredAsync(CancellationToken cancellationToken)
        {
            while (Volatile.Read(ref moved) < Volatile.Read(ref published)) { await Task.Delay(1, cancellationToken); }
        }
    }

    private sealed class FailingWatch(bool afterSubscribed) : ISignalWatch
    {
        public async IAsyncEnumerable<T> On<T>(object neuron, Func<Task> onSubscribed, [EnumeratorCancellation] CancellationToken cancellationToken = default)
            where T : Signal
        {
            if (afterSubscribed) { await onSubscribed(); }
            await Task.Yield();
            if (cancellationToken.IsCancellationRequested) { yield break; }
            throw new InvalidOperationException("watch failed");
        }
    }

    [Fact]
    public async Task ANamedSetFollowsTheRegistryAsMembersJoinAndLeave()
    {
        var ct = TestContext.Current.CancellationToken;
        var watch = new ControlledWatch();
        string[] members = ["a"];
        var reselected = new SemaphoreSlim(0);
        var sources = Sources.Named<string, RegistryChanged, string>("registry", (_, _) => Task.FromResult(members));
        await using var stream = Sources.Stream<Changed>(watch, sources, null, () => reselected.Release(), ct).GetAsyncEnumerator(ct);

        async Task<int> Next()
        {
            Assert.True(await stream.MoveNextAsync().AsTask().WaitAsync(Patience, ct));
            return stream.Current.Value;
        }

        watch.Publish("a", new Changed(1));
        Assert.Equal(1, await Next());

        members = ["a", "b"];
        watch.Publish("registry", new RegistryChanged());
        Assert.True(await reselected.WaitAsync(Patience, ct));
        watch.Publish("b", new Changed(2));
        Assert.Equal(2, await Next());

        members = ["b"];
        watch.Publish("registry", new RegistryChanged());
        Assert.True(await reselected.WaitAsync(Patience, ct));
        watch.Publish("a", new Changed(9));
        watch.Publish("b", new Changed(3));
        Assert.Equal(3, await Next());
    }

    [Fact]
    public async Task AReactionOverABurstRebuildsOnceForTheBurst()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var watch = new ControlledWatch();
        for (var i = 0; i < 5; i++) { watch.Publish("a", new Changed(i)); }
        var rebuilds = 0;
        await Sources.React<Changed>(watch, Sources.Of("a"), _ =>
        {
            switch (Interlocked.Increment(ref rebuilds))
            {
                case 2: watch.Publish("a", new Changed(99)); break;
                case 3: cts.Cancel(); break;
            }
            return Task.CompletedTask;
        }, cts.Token).WaitAsync(Patience, TestContext.Current.CancellationToken);
        // Once when registered, once for the five queued signals, once for the one posted during that rebuild.
        Assert.Equal(3, rebuilds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFailingWatchEndsTheStreamWithItsError(bool afterSubscribed)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var stream = Sources.Stream<Changed>(new FailingWatch(afterSubscribed), Sources.Of("a"), ct).GetAsyncEnumerator(ct);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await stream.MoveNextAsync().AsTask().WaitAsync(Patience, ct));
        Assert.Equal("watch failed", error.Message);
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
