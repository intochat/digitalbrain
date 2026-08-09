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

        await brain.SendAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Bind(ChatRoles.ResponderBindingId(chat), chat, ChatRoles.Responder, alpha),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForRouteTargetAsync(brain, chat, ChatRoles.Responder, alpha);

        await brain.GetGrainProxy<IChat>("main").Send(new SendMessage(CommandId.New(), "hello"));
        await Journals.WaitForAsync(
            brain, chat, JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded { Text: "scripted:alpha" });

        await Journals.WaitForAsync(
            brain, chat, JournalKind.Outgoing,
            delivery => delivery.Synapse is CapabilityRequested);
    }
}
