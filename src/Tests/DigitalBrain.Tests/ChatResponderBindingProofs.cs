using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Tests.Harness;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class ChatResponderBindingProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task EachChatAnswersThroughItsOwnBoundResponder()
    {
        var brain = fixture.BrainFor("respond");
        var chatA = NeuronId.For<IChat>(brain.Owner, "a");
        var chatB = NeuronId.For<IChat>(brain.Owner, "b");
        var alpha = new NeuronId("scriptedagent", brain.Owner, "alpha");
        var beta = new NeuronId("scriptedagent", brain.Owner, "beta");

        await brain.SendAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Bind(ChatRoles.ResponderBindingId(chatA), chatA, ChatRoles.Responder, alpha),
            TestContext.Current.CancellationToken);
        await brain.SendAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Bind(ChatRoles.ResponderBindingId(chatB), chatB, ChatRoles.Responder, beta),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForRoutesAsync(brain, chatA, ChatRoles.Responder);
        await Graphs.WaitForRoutesAsync(brain, chatB, ChatRoles.Responder);

        await brain.GetGrainProxy<IChat>("a").Send(new SendMessage(CommandId.New(), "hello a"));
        await brain.GetGrainProxy<IChat>("b").Send(new SendMessage(CommandId.New(), "hello b"));

        await Journals.WaitForAsync(
            brain, chatA, JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded { Text: "scripted:alpha" });
        await Journals.WaitForAsync(
            brain, chatB, JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded { Text: "scripted:beta" });
    }

    [Fact]
    public async Task RebindingAResponderReplacesInsteadOfAccumulating()
    {
        var brain = fixture.BrainFor("rebind-responder");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");
        var alpha = new NeuronId("scriptedagent", brain.Owner, "alpha");
        var beta = new NeuronId("scriptedagent", brain.Owner, "beta");

        await brain.SendAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Bind(ChatRoles.ResponderBindingId(chat), chat, ChatRoles.Responder, alpha),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForRouteTargetAsync(brain, chat, ChatRoles.Responder, alpha);

        await brain.SendAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Bind(ChatRoles.ResponderBindingId(chat), chat, ChatRoles.Responder, beta),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForRouteTargetAsync(brain, chat, ChatRoles.Responder, beta);

        var routes = await Graphs.RoutesAsync(brain, chat, ChatRoles.Responder);
        var route = Assert.Single(routes);
        Assert.Equal(beta, route.Target);

        await brain.GetGrainProxy<IChat>("main").Send(new SendMessage(CommandId.New(), "who answers now"));
        await Journals.WaitForAsync(
            brain, chat, JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded { Text: "scripted:beta" });
    }
}
