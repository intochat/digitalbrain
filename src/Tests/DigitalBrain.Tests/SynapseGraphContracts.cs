using DigitalBrain.Abstractions;
using DigitalBrain.Tests.Harness;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class SynapseGraphContracts(BrainClusterFixture fixture)
{
    [Fact]
    public async Task BoundRouteIsReturnedForItsSourceAndAlias()
    {
        var brain = fixture.BrainFor("graph-bind");
        var source = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var target = NeuronId.For<IProbeSink>(brain.Owner, "dash");
        var binding = Guid.NewGuid();

        await brain.SendAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Bind(binding, source, "probe.fact", target),
            TestContext.Current.CancellationToken);

        var routes = await Graphs.WaitForRoutesAsync(brain, source, "probe.fact");
        var route = Assert.Single(routes);
        Assert.Equal(target, route.Target);
        Assert.Equal(binding, route.BindingId);
        Assert.Null(route.Transform);
    }

    [Fact]
    public async Task BindIsAnsweredWithBound()
    {
        var brain = fixture.BrainFor("graph-ask");
        var source = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var target = NeuronId.For<IProbeSink>(brain.Owner, "dash");
        var binding = Guid.NewGuid();

        var answer = (Bound)await ((DigitalBrain.Client.DigitalBrainClient)brain).SendRequestAsync(
            ISynapseGraph.ForOwner(brain.Owner),
            new Bind(binding, source, "probe.fact", target),
            typeof(Bound),
            TestContext.Current.CancellationToken);

        Assert.Equal(binding, answer.BindingId);
        Assert.Equal(target, answer.Target);
    }

    [Fact]
    public async Task RoutesForUnknownSourceStayEmpty()
    {
        var brain = fixture.BrainFor("graph-empty");
        var stranger = NeuronId.For<IProbeSource>(brain.Owner, "nobody");

        var routes = await Graphs.RoutesAsync(brain, stranger, "probe.fact");

        Assert.Empty(routes);
    }
}
