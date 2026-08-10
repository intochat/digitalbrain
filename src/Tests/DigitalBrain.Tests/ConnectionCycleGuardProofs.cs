using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using DigitalBrain.Tests.Harness;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class ConnectionCycleGuardProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task SelfConnectedEchoCascadeStopsAtTheDeliveryDepthGuard()
    {
        var brain = fixture.BrainFor("cycle");
        var echo = NeuronId.For<IProbeEcho>(brain.Owner, "loop");

        await brain.SendAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(Guid.NewGuid(), echo, "probe.fact", echo),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionsAsync(brain, echo, "probe.fact");

        await brain.SendAsync<IProbeEcho>(
            "loop", new Poke("around"), TestContext.Current.CancellationToken);

        var settled = await Journals.SnapshotAfterQuietAsync(
            brain, echo, JournalKind.Incoming, TimeSpan.FromSeconds(3));
        var cascaded = settled.Count(delivery => delivery.Synapse is ProbeFact);

        Assert.InRange(cascaded, 3, DeliveryPolicy.MaximumDepth);
    }
}
