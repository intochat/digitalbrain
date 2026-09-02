using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

[GenerateSerializer]
[Alias("db.test.announced")]
public sealed record Announced(string Text) : Signal;

[GenerateSerializer]
[Alias("db.test.unheard")]
public sealed record Unheard(string Text) : Signal;

[GenerateSerializer]
[Alias("db.test.rumor")]
public sealed record Rumor(string Text) : Signal;

[GenerateSerializer]
[Alias("db.test.faulting")]
public sealed record Faulting(string Message) : Signal;

[Alias("DigitalBrain.Substrate.Tests.IAnnouncer")]
public interface IAnnouncer : INeuron
{
    [Alias(nameof(Announce))]
    Task<int> Announce(string text);

    [Alias(nameof(AnnounceUnheard))]
    Task<int> AnnounceUnheard(string text);
}

[Alias("DigitalBrain.Substrate.Tests.IEarA")]
public interface IEarA : INeuron;

[Alias("DigitalBrain.Substrate.Tests.IEarB")]
public interface IEarB : INeuron;

[Alias("DigitalBrain.Substrate.Tests.IGossip")]
public interface IGossip : INeuron
{
    [Alias(nameof(Spread))]
    Task<int> Spread(string text);
}

[Alias("DigitalBrain.Substrate.Tests.IEarC")]
public interface IEarC : INeuron;

internal sealed class Announcer(NeuronRuntime runtime) : Neuron(runtime), IAnnouncer
{
    public Task<int> Announce(string text) => BroadcastAsync(new Announced(text));

    public Task<int> AnnounceUnheard(string text) => BroadcastAsync(new Unheard(text));
}

internal sealed class EarA(NeuronRuntime runtime) : Neuron(runtime), IEarA, IHandle<Announced>, IHandle<Faulting>
{
    public Task HandleAsync(Announced signal, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task HandleAsync(Faulting signal, CancellationToken cancellationToken)
        => Task.FromException(new InvalidOperationException(signal.Message));
}

internal sealed class EarB(NeuronRuntime runtime) : Neuron(runtime), IEarB, IHandle<Announced>
{
    public Task HandleAsync(Announced signal, CancellationToken cancellationToken) => Task.CompletedTask;
}

// Declares IHandle<Rumor> for the very signal it broadcasts — the regression fixture for the
// self-receiver deadlock: broadcast excludes self instead of using the directed sender's
// local-delivery shortcut, so if the emitter's own grain type were left in its own
// receiver set, a non-reentrant activation would await a Deliver call into itself and hang
// for the full call timeout.
internal sealed class Gossip(NeuronRuntime runtime) : Neuron(runtime), IGossip, IHandle<Rumor>
{
    public Task<int> Spread(string text) => BroadcastAsync(new Rumor(text));

    public Task HandleAsync(Rumor signal, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class EarC(NeuronRuntime runtime) : Neuron(runtime), IEarC, IHandle<Rumor>
{
    public Task HandleAsync(Rumor signal, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class SignalRoutingTests
{
    private static IAnnouncer AnnouncerIn(BrainSimulation brain, string name)
        => brain.Grains.GetGrain<IAnnouncer>(
            AnnouncerId(name).ToGrainId());

    private static NeuronId AnnouncerId(string name)
        => new("announcer", new OwnerId("owner"), name);

    private static INeuronQuery Query(BrainSimulation brain, NeuronId id)
        => brain.Grains.GetGrain<INeuronQuery>(id.ToGrainId());

    [Fact]
    public async Task Deliver_WhenHandlerExists_ReturnsHandledAndJournalsIncoming()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var targetId = new NeuronId("eara", new OwnerId("owner"), "handled");
        var target = brain.Grains.GetGrain<INeuron>(targetId.ToGrainId());
        var query = Query(brain, targetId);
        var delivery = SignalDelivery.Create(
            new Announced("heard"),
            new NeuronId("caller", new OwnerId("owner"), "source"),
            sequence: 1,
            TimeProvider.System);

        var outcome = await target.Deliver(delivery, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryOutcome.Handled, outcome);
        var journaled = Assert.Single((await query.ReadJournal(JournalKind.Incoming, 0)).Delta);
        Assert.Equal(delivery.SignalId, journaled.SignalId);
    }

    [Fact]
    public async Task Deliver_WhenHandlerIsMissing_ReturnsUnhandledAndJournalsIncoming()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var targetId = new NeuronId("eara", new OwnerId("owner"), "unhandled");
        var target = brain.Grains.GetGrain<INeuron>(targetId.ToGrainId());
        var query = Query(brain, targetId);
        var delivery = SignalDelivery.Create(
            new Unheard("ignored"),
            new NeuronId("caller", new OwnerId("owner"), "source"),
            sequence: 1,
            TimeProvider.System);

        var outcome = await target.Deliver(delivery, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryOutcome.Unhandled, outcome);
        var journaled = Assert.Single((await query.ReadJournal(JournalKind.Incoming, 0)).Delta);
        Assert.Equal(delivery.SignalId, journaled.SignalId);
    }

    [Fact]
    public async Task Deliver_WhenHandlerThrows_PropagatesAndDoesNotJournalIncoming()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var targetId = new NeuronId("eara", new OwnerId("owner"), "faulted");
        var target = brain.Grains.GetGrain<INeuron>(targetId.ToGrainId());
        var query = Query(brain, targetId);
        var delivery = SignalDelivery.Create(
            new Faulting("sentinel failure"),
            new NeuronId("caller", new OwnerId("owner"), "source"),
            sequence: 1,
            TimeProvider.System);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => target.Deliver(delivery, TestContext.Current.CancellationToken));

        Assert.Equal("sentinel failure", failure.Message);
        Assert.Empty((await query.ReadJournal(JournalKind.Incoming, 0)).Delta);
    }

    // Tier 1 always addresses the "default"-named instance of a grain type (SignalRouter
    // hard-codes it), so the self-receiver hazard only bites when the emitter itself IS the
    // "default" instance: that is the one case where tier 1's own candidate resolves back to
    // the emitter's own NeuronId rather than a sibling activation.
    private static IGossip GossipDefault(BrainSimulation brain)
        => brain.Grains.GetGrain<IGossip>(
            new NeuronId("gossip", new OwnerId("owner"), "default").ToGrainId());

    [Fact]
    public async Task Broadcast_ReachesEveryNeuronTypeThatDeclaresIHandle()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });

        Assert.Equal(2, await AnnouncerIn(brain, "a").Announce("hello"));
    }

    [Fact]
    public async Task Broadcast_RecordsOneSynapsePerReceiver()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var announcer = AnnouncerIn(brain, "b");
        var query = Query(brain, AnnouncerId("b"));

        await announcer.Announce("hello");

        var synapses = await query.ReadSynapses();
        Assert.Equal(2, synapses.Count);
        Assert.All(synapses, synapse => Assert.Equal(nameof(Announced), synapse.SignalType));
    }

    [Fact]
    public async Task Broadcast_PotentiatesRatherThanDuplicatingOnTheSecondRun()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var announcer = AnnouncerIn(brain, "c");
        var query = Query(brain, AnnouncerId("c"));

        await announcer.Announce("one");
        await announcer.Announce("two");

        var synapses = await query.ReadSynapses();
        Assert.Equal(2, synapses.Count);
        Assert.All(synapses, synapse => Assert.Equal(0.755, synapse.Weight, precision: 10));
        Assert.All(synapses, synapse => Assert.Equal(2, synapse.FireCount));
    }

    [Fact]
    public async Task Broadcast_JournalsOneOutgoingEntryPerReceiver()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var announcer = AnnouncerIn(brain, "d");
        var query = Query(brain, AnnouncerId("d"));

        await announcer.Announce("hello");

        var read = await query.ReadJournal(JournalKind.Outgoing, 0);
        Assert.Equal(2, read.Delta.Count);
        Assert.Single(read.Delta.Select(delivery => delivery.CorrelationId).Distinct());
    }

    [Fact]
    public async Task Broadcast_WithNoDeclaredHandlerReachesNobodyAndRecordsNothing()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var announcer = AnnouncerIn(brain, "e");
        var query = Query(brain, AnnouncerId("e"));

        // Unheard has no IHandle<Unheard> declared anywhere in the test assembly, so tier 1
        // finds nothing, tier 2 has no learned edge yet, and tier 3 (similarity discovery)
        // is a later slice: the miss must return an empty set, not a guess.
        Assert.Equal(0, await announcer.AnnounceUnheard("hello"));
        Assert.Empty(await query.ReadSynapses());
    }

    [Fact]
    public async Task Broadcast_ExcludesTheEmitterEvenWhenTheEmitterDeclaresIHandleForTheSameSignal()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var gossip = GossipDefault(brain);
        var gossipId = new NeuronId("gossip", new OwnerId("owner"), "default");
        var query = Query(brain, gossipId);

        // Gossip declares IHandle<Rumor> and broadcasts Rumor. Pre-fix, tier 1 would include
        // gossip's own grain type among the receivers of its own broadcast — and because
        // gossip is itself the "default" instance, that candidate resolves to gossip's own
        // NeuronId, so BroadcastAsync's unconditional Deliver call would have the activation
        // await a call into itself and hang for the whole call budget. If this test times out
        // or never completes, the exclusion in SignalRouter.Resolve has regressed.
        var reached = await gossip.Spread("hello");

        // Only EarC hears it: the count and the recorded synapse both exclude gossip itself.
        Assert.Equal(1, reached);

        var synapses = await query.ReadSynapses();
        var synapse = Assert.Single(synapses);
        Assert.Equal(new NeuronId("earc", new OwnerId("owner"), "default"), synapse.Target);
    }
}
