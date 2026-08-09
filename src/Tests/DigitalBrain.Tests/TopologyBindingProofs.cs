using DigitalBrain.Abstractions;
using DigitalBrain.Introspection;
using DigitalBrain.Tests.Harness;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class TopologyBindingProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task TopologyAnswersWithTheLiveBindings()
    {
        var brain = fixture.BrainFor("topology");
        var source = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var sink = NeuronId.For<IProbeSink>(brain.Owner, "dash");
        var binding = Guid.NewGuid();

        await brain.SendAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Bind(binding, source, "probe.fact", sink),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForRoutesAsync(brain, source, "probe.fact");

        var topology = (TopologyRead)await ((DigitalBrain.Client.DigitalBrainClient)brain).SendRequestAsync(
            NeuronId.For<IIntrospection>(brain.Owner, "default"),
            new ReadTopologyRequest(),
            typeof(TopologyRead),
            TestContext.Current.CancellationToken);

        var reported = Assert.Single(topology.Bindings);
        Assert.Equal(binding, reported.BindingId);
        Assert.Equal(source.ToString(), reported.Source);
        Assert.Equal("probe.fact", reported.SynapseAlias);
        Assert.Equal(sink.ToString(), reported.Target);
        Assert.Null(reported.Transform);
    }
}
