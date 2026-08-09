using DigitalBrain.Abstractions;
using DigitalBrain.Tests.Harness;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class RouteTeardownProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task UnboundRouteStopsDelivering()
    {
        var brain = fixture.BrainFor("teardown");
        var source = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var sink = NeuronId.For<IProbeSink>(brain.Owner, "dash");
        var binding = Guid.NewGuid();

        await brain.SendAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Bind(binding, source, "probe.fact", sink),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForRoutesAsync(brain, source, "probe.fact");
        await brain.SendAsync<IProbeSource>(
            "elon", new Poke("before"), TestContext.Current.CancellationToken);
        await Journals.WaitForAsync(
            brain, sink, JournalKind.Incoming,
            delivery => delivery.Synapse is ProbeFact { Text: "before" });

        await brain.SendAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName, new Unbind(binding), TestContext.Current.CancellationToken);
        await Graphs.WaitForNoRoutesAsync(brain, source, "probe.fact");
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
    public async Task ExpiredRouteNeverDelivers()
    {
        var brain = fixture.BrainFor("expiry");
        var source = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var sink = NeuronId.For<IProbeSink>(brain.Owner, "dash");
        var graph = ISynapseGraph.ForOwner(brain.Owner);
        var alreadyPast = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1);

        await brain.SendAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Bind(Guid.NewGuid(), source, "probe.fact", sink, ExpiresAt: alreadyPast),
            TestContext.Current.CancellationToken);
        await Journals.WaitForAsync(
            brain, graph, JournalKind.Incoming, delivery => delivery.Synapse is Bind);

        var routes = await Graphs.RoutesAsync(brain, source, "probe.fact");
        Assert.Empty(routes);

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
