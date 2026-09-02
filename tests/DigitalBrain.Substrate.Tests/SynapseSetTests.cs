using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

[GenerateSerializer]
[Alias("db.test.ping")]
public sealed record Ping(string Text) : Signal;

[Alias("DigitalBrain.Substrate.Tests.IPingSource")]
public interface IPingSource : INeuron
{
    [Alias(nameof(SendTo))]
    Task SendTo(NeuronId target, string text);
}

[Alias("DigitalBrain.Substrate.Tests.IPingSink")]
public interface IPingSink : INeuron;

internal sealed class PingSource : Neuron, IPingSource
{
    public Task SendTo(NeuronId target, string text) => FireAsync(target, new Ping(text));
}

internal sealed class PingSink : Neuron, IPingSink, IHandle<Ping>
{
    public Task HandleAsync(Ping signal, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class SynapseSetTests
{
    [Fact]
    public async Task FirstFire_CreatesALearnedSynapseAtTheInitialWeightThenPotentiatesIt()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });

        var sourceId = new NeuronId("pingsource", new OwnerId("owner"), "a");
        var source = brain.Grains.GetGrain<IPingSource>(sourceId.ToGrainId());
        var sourceQuery = brain.Grains.GetGrain<INeuronQuery>(sourceId.ToGrainId());
        var sinkId = new NeuronId("pingsink", new OwnerId("owner"), "b");

        await source.SendTo(sinkId, "one");

        var synapse = Assert.Single(await sourceQuery.ReadSynapses());
        Assert.Equal(sinkId, synapse.Target);
        Assert.Equal(nameof(Ping), synapse.SignalType);
        Assert.Equal(SynapseKind.Learned, synapse.Kind);
        Assert.Equal(0.65, synapse.Weight, precision: 10);
        Assert.Equal(1, synapse.FireCount);
    }

    [Fact]
    public async Task SecondFire_PotentiatesTheSameSynapseRatherThanAddingOne()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });

        var sourceId = new NeuronId("pingsource", new OwnerId("owner"), "c");
        var source = brain.Grains.GetGrain<IPingSource>(sourceId.ToGrainId());
        var sourceQuery = brain.Grains.GetGrain<INeuronQuery>(sourceId.ToGrainId());
        var sinkId = new NeuronId("pingsink", new OwnerId("owner"), "d");

        await source.SendTo(sinkId, "one");
        await source.SendTo(sinkId, "two");

        var synapse = Assert.Single(await sourceQuery.ReadSynapses());
        Assert.Equal(0.755, synapse.Weight, precision: 10);
        Assert.Equal(2, synapse.FireCount);
    }

    [Fact]
    public async Task DistinctTargets_GetDistinctSynapses()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });

        var sourceId = new NeuronId("pingsource", new OwnerId("owner"), "e");
        var source = brain.Grains.GetGrain<IPingSource>(sourceId.ToGrainId());
        var sourceQuery = brain.Grains.GetGrain<INeuronQuery>(sourceId.ToGrainId());

        await source.SendTo(new NeuronId("pingsink", new OwnerId("owner"), "f"), "one");
        await source.SendTo(new NeuronId("pingsink", new OwnerId("owner"), "g"), "two");

        Assert.Equal(2, (await sourceQuery.ReadSynapses()).Count);
    }
}
