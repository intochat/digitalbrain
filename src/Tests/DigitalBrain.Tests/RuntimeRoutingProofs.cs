using DigitalBrain.Abstractions;
using DigitalBrain.Tests.Harness;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class RuntimeRoutingProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task EmittedFactReachesRuntimeBoundSinkWithNoCompiledHandler()
    {
        var brain = fixture.BrainFor("route");
        var source = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var sink = NeuronId.For<IProbeSink>(brain.Owner, "dash");

        await brain.SendAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Bind(Guid.NewGuid(), source, "probe.fact", sink),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForRoutesAsync(brain, source, "probe.fact");

        await brain.SendAsync<IProbeSource>(
            "elon", new Poke("routed"), TestContext.Current.CancellationToken);

        var delivered = await Journals.WaitForAsync(
            brain,
            sink,
            JournalKind.Incoming,
            delivery => delivery.Synapse is ProbeFact fact && fact.Text == "routed");
        Assert.Equal(source, delivered.Caller);
    }

    [Fact]
    public async Task RouteLookupIsInfrastructureAndLeavesNoCapabilityTraceInJournals()
    {
        var brain = fixture.BrainFor("quiet-route");
        var source = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var sink = NeuronId.For<IProbeSink>(brain.Owner, "dash");

        await brain.SendAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Bind(Guid.NewGuid(), source, "probe.fact", sink),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForRoutesAsync(brain, source, "probe.fact");
        await brain.SendAsync<IProbeSource>(
            "elon", new Poke("clean"), TestContext.Current.CancellationToken);
        await Journals.WaitForAsync(
            brain, sink, JournalKind.Incoming,
            delivery => delivery.Synapse is ProbeFact { Text: "clean" });

        var emitted = await Journals.SnapshotAfterQuietAsync(
            brain, source, JournalKind.Outgoing, TimeSpan.FromSeconds(1));
        var fact = Assert.Single(emitted);
        Assert.IsType<ProbeFact>(fact.Synapse);
    }
}
