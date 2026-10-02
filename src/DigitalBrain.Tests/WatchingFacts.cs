using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace DigitalBrain.Tests;

public class WatchingFacts
{
    [Fact]
    public async Task The_watch_loop_streams_typed_signals_until_the_source_completes()
    {
        var button = new FakeButton();
        var received = new List<Clicked>();
        var loop = Task.Run(async () =>
        {
            await foreach (var click in button.Watch<Clicked>(TestContext.Current.CancellationToken))
            {
                received.Add(click);
            }
        }, TestContext.Current.CancellationToken);

        button.Publish(new Hovered { Publisher = new("cursor") });
        button.Publish(new Clicked { Publisher = new("button-1") });
        button.Complete();
        await loop;

        var click = Assert.Single(received);
        Assert.Equal(new NeuronId("button-1"), click.Publisher);
    }

    [Fact]
    public async Task Cancelling_the_token_severs_the_loop()
    {
        var button = new FakeButton();
        using var stopping = new CancellationTokenSource();
        var loop = Task.Run(async () =>
        {
            await foreach (var _ in button.Watch<Clicked>(stopping.Token)) { }
        }, TestContext.Current.CancellationToken);

        stopping.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loop);
    }

    [Fact]
    public async Task A_publisher_resolves_back_through_the_brain()
    {
        var button = new FakeButton();
        await using var brain = new FakeBrain(new NeuronId("button-1"), button);
        button.Publish(new Clicked { Publisher = new("button-1") });
        button.Complete();

        await foreach (var click in button.Watch<Clicked>(TestContext.Current.CancellationToken))
        {
            Assert.Same(button, brain.Get<INeuron>(click.Publisher));
        }
    }

    [Fact]
    public async Task Watching_the_signal_base_type_streams_everything()
    {
        var button = new FakeButton();
        button.Publish(new Hovered { Publisher = new("cursor") });
        button.Publish(new Clicked { Publisher = new("button-1") });
        button.Complete();

        var received = new List<Signal>();
        await foreach (var signal in button.Watch<Signal>(TestContext.Current.CancellationToken))
        {
            received.Add(signal);
        }

        Assert.Equal(2, received.Count);
    }

    private sealed record Clicked : Signal;

    private sealed record Hovered : Signal;

    private sealed class FakeButton : INeuron
    {
        private readonly Channel<Signal> signals = Channel.CreateUnbounded<Signal>();

        public void Publish(Signal signal) => signals.Writer.TryWrite(signal);

        public void Complete() => signals.Writer.TryComplete();

        public async IAsyncEnumerable<T> Watch<T>(
            [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : Signal
        {
            await foreach (var signal in signals.Reader.ReadAllAsync(cancellationToken))
            {
                if (signal is T typed)
                {
                    yield return typed;
                }
            }
        }
    }

    private sealed class FakeBrain(NeuronId id, INeuron neuron) : IDigitalBrain
    {
        public T Get<T>(NeuronId requested) where T : class, INeuron =>
            requested == id ? (T)neuron : throw new InvalidOperationException(requested.ToString());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
