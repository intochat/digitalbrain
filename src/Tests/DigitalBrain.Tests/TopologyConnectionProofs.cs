using DigitalBrain.Abstractions;
using DigitalBrain.Introspection;
using DigitalBrain.Tests.Harness;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class TopologyConnectionProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task TopologyAnswersWithTheLiveConnections()
    {
        var brain = fixture.BrainFor("topology");
        var source = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var sink = NeuronId.For<IProbeSink>(brain.Owner, "dash");
        var connection = Guid.NewGuid();

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(connection, source, "probe.fact", sink),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionsAsync(brain, source, "probe.fact");

        var topology = (TopologyRead)await ((DigitalBrain.Client.DigitalBrainClient)brain).SendRequestAsync(
            NeuronId.For<IIntrospection>(brain.Owner, "default"),
            new ReadTopologyRequest(),
            typeof(TopologyRead),
            TestContext.Current.CancellationToken);

        var reported = Assert.Single(topology.Connections);
        Assert.Equal(connection, reported.ConnectionId);
        Assert.Equal(source.ToString(), reported.Source);
        Assert.Equal("probe.fact", reported.SynapseAlias);
        Assert.Equal(sink.ToString(), reported.Target);
        Assert.Null(reported.Transform);
    }

    [Fact]
    public async Task TopologyReportsTheCompiledBroadcastTierAlongsideTheGraph()
    {
        var brain = fixture.BrainFor("topology-manifest");

        var topology = (TopologyRead)await ((DigitalBrain.Client.DigitalBrainClient)brain).SendRequestAsync(
            NeuronId.For<IIntrospection>(brain.Owner, "default"),
            new ReadTopologyRequest(),
            typeof(TopologyRead),
            TestContext.Current.CancellationToken);

        Assert.Contains(
            topology.BroadcastRoutes,
            route => route.SynapseAlias == "ui.chart-point" && route.HandlerGrainType == "chart");
        Assert.Contains(
            topology.BroadcastRoutes,
            route => route.SynapseAlias == "ui.note" && route.HandlerGrainType == "chat");
    }
}
