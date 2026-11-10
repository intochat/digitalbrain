using DigitalBrain.Rules;
using DigitalBrain.Spi;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Synapses;

[Signal("test.fr.poke")]
public sealed record FrPoke(bool Boom) : Signal;

[State("fr.count")]
public sealed record FrCount(int Value)
{
    public FrCount() : this(0) { }
}

[State("fr.echo")]
public sealed record FrEcho(int Value)
{
    public FrEcho() : this(0) { }
}

[State("fr.gate")]
public sealed record FrGate(int Value)
{
    public FrGate() : this(0) { }
}

public static class FrUnit
{
    [Synapse(typeof(FrPoke), "fr.count")]
    public static FrCount Count(FrPoke p, FrCount c) => p.Boom ? throw new InvalidOperationException("boom") : c with { Value = c.Value + 1 };

    [Synapse(typeof(SynapseFailed), "fr.echo")]
    public static FrEcho Echo(SynapseFailed f, FrEcho e) => throw new InvalidOperationException("echo");

    // fr.echo also counts the pokes: its failure on synapse.failed (journaled nowhere) must not block what comes after it.
    [Synapse(typeof(FrPoke), "fr.echo")]
    public static FrEcho Heard(FrPoke p, FrEcho e) => e with { Value = e.Value + 1 };

    // A gate on the kernel's failure fact must never run: it would refuse the record of the failure.
    [Synapse(typeof(SynapseFailed), "fr.gate")]
    public static Check NoFailures(SynapseFailed f, FrGate g) => Check.Reject("failures are not gated");
}

public interface IFrPoker
{
    Task Poke(bool boom);
}

[Neuron("fr.poker")]
public sealed class FrPoker : Neuron<FrCount>, IFrPoker
{
    public Task Poke(bool boom) => Save(State with { Value = State.Value + 1 }, new FrPoke(boom));
}

public sealed class FailedReactionFacts
{
    private static readonly NeuronId Installs = NeuronId.Parse("fr/synapse.installs/default");
    private static readonly NeuronId Unit = NeuronId.Parse("fr/fr.count/default");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AFailedReactionIsRecordedOnceAndTheNextSignalStillRuns()
    {
        await using var os = OS.Local().Register(typeof(FailedReactionFacts).Assembly);
        os.Confirmations.ApproveAll();
        _ = await os.Get<IInstalls>(Installs).Install(new InstallRequest("fr.count", new Dictionary<string, string>()), Ct);
        _ = await os.Get<IInstalls>(Installs).Install(new InstallRequest("fr.echo", new Dictionary<string, string>()), Ct);
        _ = await os.Get<IInstalls>(Installs).Install(new InstallRequest("fr.gate", new Dictionary<string, string>()), Ct);
        await os.Idle();
        var poker = os.Get<IFrPoker>(NeuronId.Parse("fr/fr.poker/a"));

        await poker.Poke(true);
        await os.Idle();
        await poker.Poke(false);
        await os.Idle();
        await os.Restart();

        var fact = (SynapseFailed)Assert.Single(os.Journal(Unit.Brain).Signals(Unit), s => s.Signal is SynapseFailed).Signal;
        Assert.Equal(("fr.count", "test.fr.poke", "InvalidOperationException", "boom"), (fact.Alias, fact.Cause, fact.Error, fact.Message));
        Assert.EndsWith(".Count", fact.Function, StringComparison.Ordinal);
        Assert.NotNull(fact.CauseId);
        // Read through the state reader: after Restart the neuron is not active, and IBrainObserver.State reads live activations only.
        Assert.Equal(1, (await os.Get<IStateReader>(Unit).Read("fr.count")).GetProperty("value").GetInt32());
        // fr.echo failed on synapse.failed itself: that failure is not journaled, so the two units do not feed each other,
        // and it still heard both pokes: the failed signal was not left in front of the second one.
        var echo = NeuronId.Parse("fr/fr.echo/default");
        Assert.DoesNotContain(os.Journal(Unit.Brain).Signals(echo), s => s.Signal is SynapseFailed);
        Assert.Equal(2, (await os.Get<IStateReader>(echo).Read("fr.echo")).GetProperty("value").GetInt32());
    }
}
