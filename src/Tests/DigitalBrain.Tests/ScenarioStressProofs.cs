using DigitalBrain.Abstractions;
using DigitalBrain.Tests.Harness;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class ScenarioStressProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task OneEmissionFansOutToDirectAndTransformedTargets()
    {
        var brain = fixture.BrainFor("fanout");
        var feed = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var chart = NeuronId.For<IProbeSink>(brain.Owner, "chart");
        var archive = NeuronId.For<IProbeSink>(brain.Owner, "archive");

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(Guid.NewGuid(), feed, "probe.fact", chart, ProbeFactToItemAppended.TransformName),
            TestContext.Current.CancellationToken);
        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(Guid.NewGuid(), feed, "probe.fact", archive),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionsAsync(brain, feed, "probe.fact", connectionCount: 2);

        await brain.FireAsync<IProbeSource>(
            "elon", new Poke("shipped it"), TestContext.Current.CancellationToken);

        await Journals.WaitForAsync(
            brain, chart, JournalKind.Incoming,
            delivery => delivery.Synapse is ItemAppended { Title: "shipped it" });
        await Journals.WaitForAsync(
            brain, archive, JournalKind.Incoming,
            delivery => delivery.Synapse is ProbeFact { Text: "shipped it" });
    }

    [Fact]
    public async Task EachSourceRoutesOnlyItsOwnFacts()
    {
        var brain = fixture.BrainFor("isolation");
        var elon = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var sam = NeuronId.For<IProbeSource>(brain.Owner, "sam");
        var elonSink = NeuronId.For<IProbeSink>(brain.Owner, "elon-dash");
        var samSink = NeuronId.For<IProbeSink>(brain.Owner, "sam-dash");

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(Guid.NewGuid(), elon, "probe.fact", elonSink),
            TestContext.Current.CancellationToken);
        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(Guid.NewGuid(), sam, "probe.fact", samSink),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionsAsync(brain, elon, "probe.fact");
        await Graphs.WaitForConnectionsAsync(brain, sam, "probe.fact");

        await brain.FireAsync<IProbeSource>(
            "elon", new Poke("from elon"), TestContext.Current.CancellationToken);
        await brain.FireAsync<IProbeSource>(
            "sam", new Poke("from sam"), TestContext.Current.CancellationToken);

        await Journals.WaitForAsync(
            brain, elonSink, JournalKind.Incoming,
            delivery => delivery.Synapse is ProbeFact { Text: "from elon" });
        await Journals.WaitForAsync(
            brain, samSink, JournalKind.Incoming,
            delivery => delivery.Synapse is ProbeFact { Text: "from sam" });

        var elonSettled = await Journals.SnapshotAfterQuietAsync(
            brain, elonSink, JournalKind.Incoming, TimeSpan.FromSeconds(2));
        Assert.DoesNotContain(elonSettled, delivery => delivery.Synapse is ProbeFact { Text: "from sam" });
    }

    [Fact]
    public async Task ConnectionsOfOneOwnerNeverRouteAnotherOwnersEmissions()
    {
        var alice = fixture.BrainFor("alice");
        var bob = fixture.BrainFor("bob");
        var aliceSink = NeuronId.For<IProbeSink>(alice.Owner, "dash");
        var bobSink = NeuronId.For<IProbeSink>(bob.Owner, "dash");

        await alice.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(
                Guid.NewGuid(),
                NeuronId.For<IProbeSource>(alice.Owner, "elon"),
                "probe.fact",
                aliceSink),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionsAsync(
            alice, NeuronId.For<IProbeSource>(alice.Owner, "elon"), "probe.fact");

        await bob.FireAsync<IProbeSource>(
            "elon", new Poke("bob speaking"), TestContext.Current.CancellationToken);

        await Journals.WaitForAsync(
            bob,
            NeuronId.For<IProbeSource>(bob.Owner, "elon"),
            JournalKind.Outgoing,
            delivery => delivery.Synapse is ProbeFact { Text: "bob speaking" });
        var aliceSettled = await Journals.SnapshotAfterQuietAsync(
            alice, aliceSink, JournalKind.Incoming, TimeSpan.FromSeconds(2));
        Assert.DoesNotContain(
            aliceSettled, delivery => delivery.Synapse is ProbeFact { Text: "bob speaking" });
        var bobSettled = await Journals.SnapshotAfterQuietAsync(
            bob, bobSink, JournalKind.Incoming, TimeSpan.FromSeconds(2));
        Assert.Empty(bobSettled);
    }

    [Fact]
    public async Task ReconnectingRedirectsSubsequentEmissions()
    {
        var brain = fixture.BrainFor("rebind");
        var feed = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var first = NeuronId.For<IProbeSink>(brain.Owner, "first");
        var second = NeuronId.For<IProbeSink>(brain.Owner, "second");
        var connection = Guid.NewGuid();

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(connection, feed, "probe.fact", first),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionsAsync(brain, feed, "probe.fact");
        await brain.FireAsync<IProbeSource>(
            "elon", new Poke("to first"), TestContext.Current.CancellationToken);
        await Journals.WaitForAsync(
            brain, first, JournalKind.Incoming,
            delivery => delivery.Synapse is ProbeFact { Text: "to first" });

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(connection, feed, "probe.fact", second),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionTargetAsync(brain, feed, "probe.fact", second);
        await brain.FireAsync<IProbeSource>(
            "elon", new Poke("to second"), TestContext.Current.CancellationToken);

        await Journals.WaitForAsync(
            brain, second, JournalKind.Incoming,
            delivery => delivery.Synapse is ProbeFact { Text: "to second" });
        var firstSettled = await Journals.SnapshotAfterQuietAsync(
            brain, first, JournalKind.Incoming, TimeSpan.FromSeconds(2));
        Assert.DoesNotContain(
            firstSettled, delivery => delivery.Synapse is ProbeFact { Text: "to second" });
    }

    [Fact]
    public async Task ForeignSourcesAreRefusedByTheGraph()
    {
        var alice = fixture.BrainFor("alice-refusal");
        var bobsFeed = new NeuronId("probesource", new OwnerId("bob-refusal"), "elon");
        var aliceSink = NeuronId.For<IProbeSink>(alice.Owner, "dash");
        var graph = ISynapseGraph.ForOwner(alice.Owner);

        await alice.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(Guid.NewGuid(), bobsFeed, "probe.fact", aliceSink),
            TestContext.Current.CancellationToken);

        await Journals.WaitForAsync(
            alice, graph, JournalKind.Incoming, delivery => delivery.Synapse is Connect);
        var connections = await Graphs.ConnectionsAsync(alice, bobsFeed, "probe.fact");
        Assert.Empty(connections);
    }
}
