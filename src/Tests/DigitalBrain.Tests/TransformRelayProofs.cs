using DigitalBrain.Abstractions;
using DigitalBrain.Tests.Harness;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class TransformRelayProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task TransformedRouteDeliversAdaptedSynapseThroughJournaledRelay()
    {
        var brain = fixture.BrainFor("transform");
        var source = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var sink = NeuronId.For<IProbeSink>(brain.Owner, "chart");
        var connection = Guid.NewGuid();
        var relay = new NeuronId("relay", brain.Owner, connection.ToString("D"));

        await brain.SendAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(connection, source, "probe.fact", sink, ProbeFactToItemAppended.TransformName),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionsAsync(brain, source, "probe.fact");

        await brain.SendAsync<IProbeSource>(
            "elon", new Poke("elon posted"), TestContext.Current.CancellationToken);

        var adapted = await Journals.WaitForAsync(
            brain,
            sink,
            JournalKind.Incoming,
            delivery => delivery.Synapse is ItemAppended { Title: "elon posted" });
        Assert.Equal(relay, adapted.Caller);

        var carried = await Journals.WaitForAsync(
            brain,
            relay,
            JournalKind.Incoming,
            delivery => delivery.Synapse is ProbeFact { Text: "elon posted" });
        Assert.Equal(source, carried.Caller);
        Assert.Equal(carried.CorrelationId, adapted.CorrelationId);
    }

    [Fact]
    public async Task FailingTransformSettlesAsRefusalInsteadOfRetrying()
    {
        var brain = fixture.BrainFor("poison");
        var source = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var sink = NeuronId.For<IProbeSink>(brain.Owner, "chart");
        var connection = Guid.NewGuid();
        var relay = new NeuronId("relay", brain.Owner, connection.ToString("D"));

        await brain.SendAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(connection, source, "probe.fact", sink, PoisonTransform.TransformName),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionsAsync(brain, source, "probe.fact");

        await brain.SendAsync<IProbeSource>(
            "elon", new Poke("toxic"), TestContext.Current.CancellationToken);

        await Journals.WaitForAsync(
            brain,
            relay,
            JournalKind.Incoming,
            delivery => delivery.Synapse is ProbeFact { Text: "toxic" });
        var settled = await Journals.SnapshotAfterQuietAsync(
            brain, sink, JournalKind.Incoming, TimeSpan.FromSeconds(2));
        Assert.Empty(settled);
    }
}
