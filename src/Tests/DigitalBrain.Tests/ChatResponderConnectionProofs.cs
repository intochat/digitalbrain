using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Tests.Harness;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class ChatResponderConnectionProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task EachChatAnswersThroughItsOwnBoundResponder()
    {
        var brain = fixture.BrainFor("respond");
        var chatA = NeuronId.For<IChat>(brain.Owner, "a");
        var chatB = NeuronId.For<IChat>(brain.Owner, "b");
        var alpha = new NeuronId("scriptedagent", brain.Owner, "alpha");
        var beta = new NeuronId("scriptedagent", brain.Owner, "beta");

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(ChatRoles.ResponderConnectionId(chatA), chatA, ChatRoles.Responder, alpha),
            TestContext.Current.CancellationToken);
        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(ChatRoles.ResponderConnectionId(chatB), chatB, ChatRoles.Responder, beta),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionsAsync(brain, chatA, ChatRoles.Responder);
        await Graphs.WaitForConnectionsAsync(brain, chatB, ChatRoles.Responder);

        await brain.GetGrainProxy<IChat>("a").Send(new SendMessage(CommandId.New(), "hello a", TestActors.Operator));
        await brain.GetGrainProxy<IChat>("b").Send(new SendMessage(CommandId.New(), "hello b", TestActors.Operator));

        await Journals.WaitForAsync(
            brain, chatA, JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded { Text: "scripted:alpha", Author: "alpha" });
        await Journals.WaitForAsync(
            brain, chatB, JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded { Text: "scripted:beta", Author: "beta" });
    }

    [Fact]
    public async Task ReconnectingAResponderReplacesInsteadOfAccumulating()
    {
        var brain = fixture.BrainFor("rebind-responder");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");
        var alpha = new NeuronId("scriptedagent", brain.Owner, "alpha");
        var beta = new NeuronId("scriptedagent", brain.Owner, "beta");

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(ChatRoles.ResponderConnectionId(chat), chat, ChatRoles.Responder, alpha),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionTargetAsync(brain, chat, ChatRoles.Responder, alpha);

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(ChatRoles.ResponderConnectionId(chat), chat, ChatRoles.Responder, beta),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionTargetAsync(brain, chat, ChatRoles.Responder, beta);

        var connections = await Graphs.ConnectionsAsync(brain, chat, ChatRoles.Responder);
        var connection = Assert.Single(connections);
        Assert.Equal(beta, connection.Target);

        await brain.GetGrainProxy<IChat>("main").Send(new SendMessage(CommandId.New(), "who answers now", TestActors.Operator));
        await Journals.WaitForAsync(
            brain, chat, JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded { Text: "scripted:beta", Author: "beta" });
    }
}
