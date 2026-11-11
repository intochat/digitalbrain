using DigitalBrain.Modules;
using DigitalBrain.Neurons;
using DigitalBrain;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;

namespace DigitalBrain.Framework.Tests.E2E;

public sealed class JournalTailFixture : BothRuntimesFixture
{
    protected override IReadOnlyList<Type> Modules => [typeof(JournalTailModule)];
}

/// <summary><see cref="Spi.ISignalHistory.Watch"/> tails the brain journal by the same rule on TestOS and on the silo.</summary>
public sealed class JournalTailFacts(JournalTailFixture fixture) : IClassFixture<JournalTailFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task WatchYieldsSignalsAppendedAfterSince(NeuronRuntimeKind kind)
    {
        var runtime = await fixture.Runtime(kind);
        var counter = runtime.Get<ITailCounter>(runtime.Id("tail.counter", kind.ToString()));
        var tail = runtime.Get<ITailWatcher>(runtime.Id("tail.watcher", kind.ToString()));
        await counter.Add(1);
        await runtime.Idle();
        var since = await tail.Last();

        var watching = tail.Watch(since, 2);
        await counter.Add(2);
        await counter.Add(3);
        var seen = await watching.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        Assert.Equal([2, 3], seen.Select(item => item.Value));
        Assert.All(seen, item => Assert.True(item.Sequence > since));
        Assert.True(seen[0].Sequence < seen[1].Sequence);
    }
}

[Module("journal-tail-conformance")]
public sealed class JournalTailModule : Module
{
    public static ModuleManifest Manifest { get; } = new([], [], [Need.State("tail-count")], [], []);
}

[Signal("tail.counted")]
public sealed record TailCounted(int Value) : Signal;

public sealed record TailCount
{
    public int Value { get; init; }
}

/// <summary>A watched signal: its brain sequence and counted value.</summary>
public sealed record Tailed(long Sequence, int Value);

public interface ITailCounter
{
    Task Add(int value);
}

public interface ITailWatcher
{
    Task<long> Last();

    Task<Tailed[]> Watch(long since, int count);
}

[Neuron("tail.counter", State = "tail-count")]
public sealed class TailCounterNeuron : Neuron<TailCount>, ITailCounter
{
    public Task Add(int value) => Save(State with { Value = value }, new TailCounted(value));
}

/// <summary>Tails the brain journal for the facts.</summary>
[Neuron("tail.watcher"), StatelessNeuron]
public sealed class TailWatcherNeuron(ISignalHistory history) : Neuron, ITailWatcher
{
    public async Task<long> Last() => (await history.Read(long.MaxValue, 1, CancellationToken.None)).Last;

    public async Task<Tailed[]> Watch(long since, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        List<Tailed> seen = [];
        await foreach (var item in history.Watch(since, timeout.Token))
        {
            if (item.Signal is TailCounted counted) { seen.Add(new Tailed(item.Sequence, counted.Value)); }
            if (seen.Count == count) { break; }
        }

        return [.. seen];
    }
}
