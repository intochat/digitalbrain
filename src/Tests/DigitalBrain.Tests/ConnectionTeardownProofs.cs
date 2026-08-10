using DigitalBrain.Abstractions;
using DigitalBrain.Tests.Harness;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class ConnectionTeardownProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task DisconnectStopsDelivering()
    {
        var brain = fixture.BrainFor("teardown");
        var source = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var sink = NeuronId.For<IProbeSink>(brain.Owner, "dash");
        var connection = Guid.NewGuid();

        await brain.SendAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(connection, source, "probe.fact", sink),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionsAsync(brain, source, "probe.fact");
        await brain.SendAsync<IProbeSource>(
            "elon", new Poke("before"), TestContext.Current.CancellationToken);
        await Journals.WaitForAsync(
            brain, sink, JournalKind.Incoming,
            delivery => delivery.Synapse is ProbeFact { Text: "before" });

        await brain.SendAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName, new Disconnect(connection), TestContext.Current.CancellationToken);
        await Graphs.WaitForNoConnectionsAsync(brain, source, "probe.fact");
        await brain.SendAsync<IProbeSource>(
            "elon", new Poke("after"), TestContext.Current.CancellationToken);

        await Journals.WaitForAsync(
            brain, source, JournalKind.Outgoing,
            delivery => delivery.Synapse is ProbeFact { Text: "after" });
        var settled = await Journals.SnapshotAfterQuietAsync(
            brain, sink, JournalKind.Incoming, TimeSpan.FromSeconds(2));
        Assert.DoesNotContain(settled, delivery => delivery.Synapse is ProbeFact { Text: "after" });
    }

    [Fact]
    public async Task ExpiredConnectionNeverDelivers()
    {
        var brain = fixture.BrainFor("expiry");
        var source = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var sink = NeuronId.For<IProbeSink>(brain.Owner, "dash");
        var graph = ISynapseGraph.ForOwner(brain.Owner);
        var alreadyPast = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1);

        await brain.SendAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(Guid.NewGuid(), source, "probe.fact", sink, ExpiresAt: alreadyPast),
            TestContext.Current.CancellationToken);
        await Journals.WaitForAsync(
            brain, graph, JournalKind.Incoming, delivery => delivery.Synapse is Connect);

        var connections = await Graphs.ConnectionsAsync(brain, source, "probe.fact");
        Assert.Empty(connections);

        await brain.SendAsync<IProbeSource>(
            "elon", new Poke("stale"), TestContext.Current.CancellationToken);
        await Journals.WaitForAsync(
            brain, source, JournalKind.Outgoing,
            delivery => delivery.Synapse is ProbeFact { Text: "stale" });
        var settled = await Journals.SnapshotAfterQuietAsync(
            brain, sink, JournalKind.Incoming, TimeSpan.FromSeconds(2));
        Assert.DoesNotContain(settled, delivery => delivery.Synapse is ProbeFact { Text: "stale" });
    }
}
