using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Tests.Harness;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class ChatNoteProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task NotePostsALineIntoTheTranscript()
    {
        var brain = fixture.BrainFor("chat-note");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");

        await brain.FireAsync(chat, new Note("the tea is ready"), TestContext.Current.CancellationToken);

        await Journals.WaitForAsync(
            brain, chat, JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded { Text: "the tea is ready", Author: "main" });

        var transcript = await brain.GetGrainProxy<IChat>("main").Read();
        Assert.Contains(
            transcript.Turns,
            turn => !turn.FromUser && turn.Text == "the tea is ready");
    }

    [Fact]
    public async Task TimerCardPostsAClockOfferIntoTheTranscript()
    {
        var brain = fixture.BrainFor("chat-timer-card");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");
        var dueAt = DateTimeOffset.UtcNow.AddMinutes(5);

        await brain.FireAsync(chat, new TimerCard("tea in five", dueAt), TestContext.Current.CancellationToken);

        await Journals.WaitForAsync(
            brain, chat, JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded { Timers.Length: > 0, Author: "main" } posted
                && posted.Timers[0].Label == "tea in five"
                && posted.Timers[0].DueAt == dueAt);

        var transcript = await brain.GetGrainProxy<IChat>("main").Read();
        Assert.Contains(
            transcript.Turns,
            turn => turn.Timers is { Length: > 0 } offers
                && offers[0].Label == "tea in five"
                && offers[0].DueAt == dueAt);
    }
}
