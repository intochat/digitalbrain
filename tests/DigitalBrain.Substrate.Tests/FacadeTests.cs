using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

public sealed class FacadeTests
{
    [Fact]
    public async Task GetSynapsesAsync_ReturnsTheSubjectNeuronsEdges()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });

        var announcerId = new NeuronId("announcer", new OwnerId(DigitalBrainNames.DefaultOwner), "facade");
        var announcer = brain.Grains.GetGrain<IAnnouncer>(announcerId.ToGrainId());

        await announcer.Announce("hello");

        var synapses = await brain.Brain.GetSynapsesAsync(announcerId, TestContext.Current.CancellationToken);

        Assert.Equal(2, synapses.Count);
        Assert.All(synapses, synapse => Assert.Equal(announcerId, synapse.Source));
    }

    [Fact]
    public async Task GetSynapsesAsync_RefusesASubjectOwnedBySomeoneElse()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });

        var strangerId = new NeuronId("announcer", new OwnerId("someone-else"), "facade");

        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => brain.Brain.GetSynapsesAsync(strangerId, TestContext.Current.CancellationToken));
    }
}
