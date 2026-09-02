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

public sealed class SignalRoutingTests
{
    private static IAnnouncer AnnouncerIn(BrainSimulation brain, string name)
        => brain.Grains.GetGrain<IAnnouncer>(
            new NeuronId("announcer", new OwnerId("owner"), name).ToGrainId());

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
}
