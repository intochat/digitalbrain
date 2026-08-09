using DigitalBrain.Abstractions;
using DigitalBrain.Tests.Harness;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class ButtonIdentityProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task ClickedButtonEmitsItsActivation()
    {
        var brain = fixture.BrainFor("button");
        var button = NeuronId.For<IButton>(brain.Owner, "vote-yes");
        var offer = CommandId.New();

        await brain.SendAsync<IButton>(
            "vote-yes",
            new ButtonClicked(offer, "vote-yes", "vote"),
            TestContext.Current.CancellationToken);

        var activated = await Journals.WaitForAsync(
            brain, button, JournalKind.Outgoing,
            delivery => delivery.Synapse is ButtonActivated { Action: "vote" } fact
                && fact.OfferCommandId == offer
                && fact.Button == button);
        Assert.IsType<ButtonActivated>(activated.Synapse);
    }

    [Fact]
    public async Task RoutedClickReachesTheBoundSink()
    {
        var brain = fixture.BrainFor("button-route");
        var button = NeuronId.For<IButton>(brain.Owner, "complete-task");
        var task = NeuronId.For<IProbeSink>(brain.Owner, "task");

        await brain.SendAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Bind(Guid.NewGuid(), button, ButtonActivated.AliasName, task),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForRoutesAsync(brain, button, ButtonActivated.AliasName);

        await brain.SendAsync<IButton>(
            "complete-task",
            new ButtonClicked(CommandId.New(), "complete-task", "user-action"),
            TestContext.Current.CancellationToken);

        var delivered = await Journals.WaitForAsync(
            brain, task, JournalKind.Incoming,
            delivery => delivery.Synapse is ButtonActivated { Action: "user-action" });
        Assert.Equal(button, delivered.Caller);
    }
}
