using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Tests.Harness;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class ChatShowTimeProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task OfferedTimeButtonAnswersIntoItsChatThroughItsArmedBinding()
    {
        var brain = fixture.BrainFor("showtime");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");
        var command = CommandId.New();

        await brain.GetGrainProxy<IChat>("main").Send(new SendMessage(command, "show me a time button"));
        await Journals.WaitForAsync(
            brain, chat, JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded { Buttons.Length: > 0 } offer
                && offer.CommandId == command);

        // Clicking immediately after seeing the offer is the real-world sequence: the
        // chat must have confirmed the arming before the offer became visible.
        var buttonName = ChatButtons.OfferedInstanceName("main", command, "show-time");
        await brain.SendAsync<IButton>(
            buttonName,
            new ButtonClicked(command, "show-time", "show-time"),
            TestContext.Current.CancellationToken);

        await Journals.WaitForAsync(
            brain, chat, JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded answered
                && answered.Text.StartsWith("Current UTC time", StringComparison.Ordinal));
    }
}
