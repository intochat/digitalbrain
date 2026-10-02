using System.Threading.Channels;

namespace DigitalBrain.Tests;

public class WatchingFacts
{
    [Fact]
    public async Task A_typed_watch_streams_only_that_signal_type()
    {
        var button = new FakeNeuron();
        await using var clicks = await button.Watch<Clicked>();

        var synapse = Assert.Single(button.Synapses);
        synapse.Publish(new Hovered { Publisher = new("cursor") });
        synapse.Publish(new Clicked { Publisher = new("button-1") });
        synapse.Publish(new Hovered { Publisher = new("cursor") });
        synapse.Complete();

        var received = new List<Clicked>();
        await foreach (var signal in clicks.Signals(TestContext.Current.CancellationToken))
        {
            received.Add(signal);
        }

        var click = Assert.Single(received);
        Assert.Equal(new NeuronId("button-1"), click.Publisher);
    }

    [Fact]
    public async Task A_typed_watch_pulls_without_pushing_anywhere()
    {
        var button = new FakeNeuron();
        await using var clicks = await button.Watch<Clicked>();

        // The observer the typed watch supplies is inert: pull is the only consumer.
        await Assert.Single(button.Synapses).Observer
            .OnSignalAsync(new Clicked { Publisher = new("button-1") });
    }

    [Fact]
    public async Task Disposing_the_typed_synapse_severs_the_underlying_one()
    {
        var button = new FakeNeuron();
        var clicks = await button.Watch<Clicked>();

        await clicks.DisposeAsync();

        Assert.True(Assert.Single(button.Synapses).Severed);
    }

    [Fact]
    public async Task Each_typed_watch_forms_its_own_severable_synapse()
    {
        var button = new FakeNeuron();
        await using var first = await button.Watch<Clicked>();
        await using var second = await button.Watch<Clicked>();

        Assert.Equal(2, button.Synapses.Count);
        Assert.NotSame(button.Synapses[0].Observer, button.Synapses[1].Observer);
    }

    [Fact]
    public async Task The_typed_synapse_completes_with_the_underlying_one()
    {
        var button = new FakeNeuron();
        await using var clicks = await button.Watch<Clicked>();

        var synapse = Assert.Single(button.Synapses);
        Assert.False(clicks.Completion.IsCompleted);
        synapse.CompletionSource.SetResult();
        await clicks.Completion;
    }

    [Fact]
    public async Task Cancelling_the_read_stops_it_without_severing_the_synapse()
    {
        var button = new FakeNeuron();
        await using var clicks = await button.Watch<Clicked>();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in clicks.Signals(cancellation.Token)) { }
        });

        Assert.False(Assert.Single(button.Synapses).Severed);
    }

    private sealed record Clicked : Signal;

    private sealed record Hovered : Signal;

    private sealed class FakeNeuron : INeuron
    {
        public List<FakeSynapse> Synapses { get; } = [];

        public Task<ISynapse> Watch(INeuronObserver observer)
        {
            var synapse = new FakeSynapse(observer);
            Synapses.Add(synapse);
            return Task.FromResult<ISynapse>(synapse);
        }

        public Task Unwatch(INeuronObserver observer)
        {
            Synapses.RemoveAll(synapse => ReferenceEquals(synapse.Observer, observer));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSynapse(INeuronObserver observer) : ISynapse
    {
        private readonly Channel<Signal> channel = Channel.CreateUnbounded<Signal>();

        public INeuronObserver Observer { get; } = observer;

        public bool Severed { get; private set; }

        public TaskCompletionSource CompletionSource { get; } = new();

        public void Publish(Signal signal) => channel.Writer.TryWrite(signal);

        public void Complete() => channel.Writer.TryComplete();

        public IAsyncEnumerable<Signal> Signals(CancellationToken cancellationToken = default) =>
            channel.Reader.ReadAllAsync(cancellationToken);

        public Task Completion => CompletionSource.Task;

        public ValueTask DisposeAsync()
        {
            Severed = true;
            channel.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
