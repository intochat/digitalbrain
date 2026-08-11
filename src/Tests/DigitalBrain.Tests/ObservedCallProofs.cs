using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Tests.Harness;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class ObservedCallProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task ResponderGrainCallIsJournaledAsACapabilityFact()
    {
        var brain = fixture.BrainFor("observed");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");
        var alpha = new NeuronId("scriptedagent", brain.Owner, "alpha");

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(ChatRoles.ResponderConnectionId(chat), chat, ChatRoles.Responder, alpha),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionTargetAsync(brain, chat, ChatRoles.Responder, alpha);

        await brain.GetGrainProxy<IChat>("main").Send(new SendMessage(CommandId.New(), "hello", TestActors.Operator));
        await Journals.WaitForAsync(
            brain, chat, JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded { Text: "scripted:alpha" });

        await Journals.WaitForAsync(
            brain, chat, JournalKind.Outgoing,
            delivery => delivery.Synapse is CapabilityRequested);
    }
}
