using DigitalBrain.Abstractions;
using DigitalBrain.Tests.Harness;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class SynapseGraphContracts(BrainClusterFixture fixture)
{
    [Fact]
    public async Task ConnectionIsReturnedForItsSourceAndAlias()
    {
        var brain = fixture.BrainFor("graph-connect");
        var source = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var target = NeuronId.For<IProbeSink>(brain.Owner, "dash");
        var connectionId = Guid.NewGuid();

        await brain.SendAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(connectionId, source, "probe.fact", target),
            TestContext.Current.CancellationToken);

        var connections = await Graphs.WaitForConnectionsAsync(brain, source, "probe.fact");
        var connection = Assert.Single(connections);
        Assert.Equal(target, connection.Target);
        Assert.Equal(connectionId, connection.ConnectionId);
        Assert.Null(connection.Transform);
    }

    [Fact]
    public async Task ConnectIsAnsweredWithConnected()
    {
        var brain = fixture.BrainFor("graph-ask");
        var source = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var target = NeuronId.For<IProbeSink>(brain.Owner, "dash");
        var connection = Guid.NewGuid();

        var answer = (Connected)await ((DigitalBrain.Client.DigitalBrainClient)brain).SendRequestAsync(
            ISynapseGraph.ForOwner(brain.Owner),
            new Connect(connection, source, "probe.fact", target),
            typeof(Connected),
            TestContext.Current.CancellationToken);

        Assert.Equal(connection, answer.ConnectionId);
        Assert.Equal(target, answer.Target);
    }

    [Fact]
    public async Task ConnectionsFromUnknownSourceStayEmpty()
    {
        var brain = fixture.BrainFor("graph-empty");
        var stranger = NeuronId.For<IProbeSource>(brain.Owner, "nobody");

        var connections = await Graphs.ConnectionsAsync(brain, stranger, "probe.fact");

        Assert.Empty(connections);
    }
}
