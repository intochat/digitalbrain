using DigitalBrain.Neurons;
using System.Collections.Concurrent;
using DigitalBrain.Rules;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Rules;

public sealed record CheckCounterState
{
    public int Total { get; init; }
}

[Signal("test.check.added")]
public sealed record Added(int N) : Signal;

public sealed record Positive(int N) : Rule("positive")
{
    public override bool Broken => N <= 0;

    public override string Message => $"{N} is not positive.";
}

public interface ICheckCounter
{
    Task Add(int n);

    Task<int> Total();
}

[Neuron("check.counter")]
public sealed class CheckCounter : Neuron<CheckCounterState>, ICheckCounter
{
    public async Task Add(int n)
    {
        await Check("add", new Positive(n));
        await Save(State with { Total = State.Total + n }, new Added(n));
    }

    public Task<int> Total() => Task.FromResult(State.Total);
}

// Ephemeral signals reach cluster listeners, not synapses (TestNeuronContext.PublishEphemeral), so the listener is one.
public sealed class NeuronCheckFacts
{
    private static readonly NeuronId Counter = NeuronId.Parse("b/check.counter/c");

    [Fact]
    public async Task RejectedTurnIsHeardAndChangesNothing()
    {
        await using var os = OS.Local().Register(typeof(CheckCounter).Assembly);
        var counter = os.Get<ICheckCounter>(Counter);
        var heard = new ConcurrentQueue<string>();
        await using var listener = await os.ClusterListeners.Listen<Rejected>("check", (_, rejected, _) =>
        {
            heard.Enqueue(rejected.Operation);
            return Task.CompletedTask;
        });
        await counter.Add(2);
        var error = await Assert.ThrowsAsync<RuleViolationException>(() => counter.Add(-1));
        await os.Idle();
        Assert.Equal(["positive"], error.Rejected.Rules);
        Assert.Equal(2, await counter.Total());
        Assert.Equal(["add"], heard);
        Assert.DoesNotContain(os.Journal(Counter.Brain).Signals(Counter), entry => entry.Signal is Rejected);
    }
}
