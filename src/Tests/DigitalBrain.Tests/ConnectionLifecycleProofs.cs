using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Tests.Harness;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class ConnectionLifecycleProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task ConnectWithoutAnIdentityIsRefused()
    {
        var brain = fixture.BrainFor("lifecycle-connect-empty");
        var graph = ISynapseGraph.ForOwner(brain.Owner);
        var source = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var sink = NeuronId.For<IProbeSink>(brain.Owner, "feed");

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(Guid.Empty, source, "probe.fact", sink),
            TestContext.Current.CancellationToken);
        await Journals.WaitForAsync(
            brain, graph, JournalKind.Incoming,
            delivery => delivery.Synapse is Connect { ConnectionId: var id } && id == Guid.Empty);

        Assert.Empty(await Graphs.ConnectionsAsync(brain, source, "probe.fact"));
    }

    [Fact]
    public async Task DisconnectWithoutAnIdentityIsRefused()
    {
        var brain = fixture.BrainFor("lifecycle-disconnect-empty");
        var graph = ISynapseGraph.ForOwner(brain.Owner);

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Disconnect(Guid.Empty),
            TestContext.Current.CancellationToken);
        await Journals.WaitForAsync(
            brain, graph, JournalKind.Incoming,
            delivery => delivery.Synapse is Disconnect { ConnectionId: var id } && id == Guid.Empty);

        var outgoing = await brain.ReadJournalAsync(
            graph, JournalKind.Outgoing, cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(
            outgoing.Delta,
            delivery => delivery.Synapse is Disconnected { ConnectionId: var id } && id == Guid.Empty);
    }

    [Fact]
    public async Task DisconnectOfAnUnknownConnectionIsRefused()
    {
        var brain = fixture.BrainFor("lifecycle-disconnect-unknown");
        var graph = ISynapseGraph.ForOwner(brain.Owner);
        var unknown = Guid.NewGuid();

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Disconnect(unknown),
            TestContext.Current.CancellationToken);
        await Journals.WaitForAsync(
            brain, graph, JournalKind.Incoming,
            delivery => delivery.Synapse is Disconnect { ConnectionId: var id } && id == unknown);

        var outgoing = await brain.ReadJournalAsync(
            graph, JournalKind.Outgoing, cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(
            outgoing.Delta,
            delivery => delivery.Synapse is Disconnected { ConnectionId: var id } && id == unknown);
    }

    [Fact]
    public async Task UnroutedButtonClickIsRefusedInsteadOfVanishing()
    {
        var brain = fixture.BrainFor("lifecycle-dead-button");
        var button = NeuronId.For<IButton>(brain.Owner, "orphan");

        await brain.FireAsync(
            button,
            new ButtonClicked(CommandId.New(), "orphan", "noop"),
            TestContext.Current.CancellationToken);
        await Journals.WaitForAsync(
            brain, button, JournalKind.Incoming,
            delivery => delivery.Synapse is ButtonClicked);

        var outgoing = await brain.ReadJournalAsync(
            button, JournalKind.Outgoing, cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(outgoing.Delta, delivery => delivery.Synapse is ButtonActivated);
    }

    [Fact]
    public async Task AnExpiredConnectionLeavesTheGraph()
    {
        var brain = fixture.BrainFor("lifecycle-expiry");
        var source = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var sink = NeuronId.For<IProbeSink>(brain.Owner, "feed");

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(
                Guid.NewGuid(),
                source,
                "probe.fact",
                sink,
                Transform: null,
                ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(2)),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionsAsync(brain, source, "probe.fact");

        await Graphs.WaitForNoConnectionsAsync(brain, source, "probe.fact");
    }

    [Fact]
    public async Task OfferedButtonConnectionsCarryAnExpiry()
    {
        var brain = fixture.BrainFor("lifecycle-offer");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");
        var command = CommandId.New();

        await brain.GetGrainProxy<IChat>("main").Send(new SendMessage(command, "show me a time button"));
        await Journals.WaitForAsync(
            brain, chat, JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded { Buttons.Length: > 0 } offer
                && offer.CommandId == command);

        var button = NeuronId.For<IButton>(
            brain.Owner, ChatButtons.OfferedInstanceName("main", command, "show-time"));
        var routes = await Graphs.ConnectionsAsync(brain, button, ButtonActivated.AliasName);

        var armed = Assert.Single(routes);
        Assert.NotNull(armed.ExpiresAt);
        Assert.InRange(
            armed.ExpiresAt!.Value,
            DateTimeOffset.UtcNow.AddHours(23),
            DateTimeOffset.UtcNow.AddHours(25));
    }
}
