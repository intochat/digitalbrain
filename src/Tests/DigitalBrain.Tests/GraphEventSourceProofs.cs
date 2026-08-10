using DigitalBrain.Abstractions;
using DigitalBrain.Tests.Harness;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class GraphEventSourceProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task GraphOutgoingJournalCarriesConnectedAndDisconnected()
    {
        var brain = fixture.BrainFor("graph-events");
        var graph = ISynapseGraph.ForOwner(brain.Owner);
        var source = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var sink = NeuronId.For<IProbeSink>(brain.Owner, "dash");
        var connectionId = Guid.NewGuid();

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(connectionId, source, "probe.fact", sink),
            TestContext.Current.CancellationToken);

        var connected = await Journals.WaitForAsync(
            brain, graph, JournalKind.Outgoing,
            delivery => delivery.Synapse is Connected live && live.ConnectionId == connectionId);
        Assert.Equal(sink, ((Connected)connected.Synapse).Target);

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Disconnect(connectionId),
            TestContext.Current.CancellationToken);

        await Journals.WaitForAsync(
            brain, graph, JournalKind.Outgoing,
            delivery => delivery.Synapse is Disconnected gone && gone.ConnectionId == connectionId);
    }
}
