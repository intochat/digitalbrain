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

internal sealed class Announcer : Neuron, IAnnouncer
{
    public Task<int> Announce(string text) => BroadcastAsync(new Announced(text));

    public Task<int> AnnounceUnheard(string text) => BroadcastAsync(new Unheard(text));
}

internal sealed class EarA : Neuron, IEarA, IHandle<Announced>
{
    public Task HandleAsync(Announced signal, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class EarB : Neuron, IEarB, IHandle<Announced>
{
    public Task HandleAsync(Announced signal, CancellationToken cancellationToken) => Task.CompletedTask;
}

// Declares IHandle<Rumor> for the very signal it broadcasts — the regression fixture for the
// self-receiver deadlock: BroadcastAsync's Deliver call has no self-shortcut (unlike
// FireAsync's DeliverToAsync), so if the emitter's own grain type were left in its own
// receiver set, a non-reentrant activation would await a Deliver call into itself and hang
// for the full call timeout.
internal sealed class Gossip : Neuron, IGossip, IHandle<Rumor>
{
    public Task<int> Spread(string text) => BroadcastAsync(new Rumor(text));

    public Task HandleAsync(Rumor signal, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class EarC : Neuron, IEarC, IHandle<Rumor>
{
    public Task HandleAsync(Rumor signal, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class SignalRoutingTests
{
    private static IAnnouncer AnnouncerIn(BrainSimulation brain, string name)
        => brain.Grains.GetGrain<IAnnouncer>(
            new NeuronId("announcer", new OwnerId("owner"), name).ToGrainId());

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

        await announcer.Announce("hello");

        var synapses = await announcer.ReadSynapses();
        Assert.Equal(2, synapses.Count);
        Assert.All(synapses, synapse => Assert.Equal(nameof(Announced), synapse.SignalType));
    }

    [Fact]
    public async Task Broadcast_PotentiatesRatherThanDuplicatingOnTheSecondRun()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var announcer = AnnouncerIn(brain, "c");

        await announcer.Announce("one");
        await announcer.Announce("two");

        var synapses = await announcer.ReadSynapses();
        Assert.Equal(2, synapses.Count);
        Assert.All(synapses, synapse => Assert.Equal(0.755, synapse.Weight, precision: 10));
        Assert.All(synapses, synapse => Assert.Equal(2, synapse.FireCount));
    }

    [Fact]
    public async Task Broadcast_JournalsOneOutgoingEntryPerReceiver()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var announcer = AnnouncerIn(brain, "d");

        await announcer.Announce("hello");

        var read = await announcer.ReadJournal(JournalKind.Outgoing, 0);
        Assert.Equal(2, read.Delta.Count);
        Assert.Single(read.Delta.Select(delivery => delivery.CorrelationId).Distinct());
    }

    [Fact]
    public async Task Broadcast_WithNoDeclaredHandlerReachesNobodyAndRecordsNothing()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var announcer = AnnouncerIn(brain, "e");

        // Unheard has no IHandle<Unheard> declared anywhere in the test assembly, so tier 1
        // finds nothing, tier 2 has no learned edge yet, and tier 3 (similarity discovery)
        // is a later slice: the miss must return an empty set, not a guess.
        Assert.Equal(0, await announcer.AnnounceUnheard("hello"));
        Assert.Empty(await announcer.ReadSynapses());
    }

    [Fact]
    public async Task Broadcast_ExcludesTheEmitterEvenWhenTheEmitterDeclaresIHandleForTheSameSignal()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var gossip = GossipDefault(brain);

        // Gossip declares IHandle<Rumor> and broadcasts Rumor. Pre-fix, tier 1 would include
        // gossip's own grain type among the receivers of its own broadcast — and because
        // gossip is itself the "default" instance, that candidate resolves to gossip's own
        // NeuronId, so BroadcastAsync's unconditional Deliver call would have the activation
        // await a call into itself and hang for the whole call budget. If this test times out
        // or never completes, the exclusion in SignalRouter.Resolve has regressed.
        var reached = await gossip.Spread("hello");

        // Only EarC hears it: the count and the recorded synapse both exclude gossip itself.
        Assert.Equal(1, reached);

        var synapses = await gossip.ReadSynapses();
        var synapse = Assert.Single(synapses);
        Assert.Equal(new NeuronId("earc", new OwnerId("owner"), "default"), synapse.Target);
    }
}
