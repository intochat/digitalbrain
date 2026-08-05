using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class ImplicitStreamWakeTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<StreamIngressAdapter>()
            .AddModule<GratitudeNotes>()
            .AddModule<StreamWakeLedger>();

    [Fact(DisplayName =
        "Implicit stream wake (Stage-1: no Core streams — ingress adapter hears ExternalStreamTick first): dormant GratitudeNotes wakes on SlackReactionAdded")]
    public async Task IngressAdapterJournalsFirstThenWakesDormantConsumer()
    {
        var ct = Cancellation;
        var context = "slack-workspace";
        var session = Brain.Session(context);
        var ingressId = new NeuronId("streamingressadapter", context);
        var gratitudeId = new NeuronId("gratitudenotes", context);
        var ledgerId = new NeuronId("streamwakeledger", context);

        // Prove dormancy path: deactivate consumer before first event.
        await session.EmitAsync(
            new ExternalStreamTick(
                StreamId: "slack:reactions",
                EventType: "slack.reaction_added",
                Payload: "general|ada|🙏|rx-1",
                OwnerContext: context),
            ct);

        // Warm once so grain exists, then deactivate for wake proof on second tick... 
        // First tick already activates; re-prove wake after explicit deactivation.
        await WaitForJournalAsync(
            gratitudeId,
            reading => reading.AllSaid<NoteCaptured>().Count == 1,
            "first reaction processed",
            ct);

        await DeactivateAsync([gratitudeId, ingressId], ct);

        await session.EmitAsync(
            new ExternalStreamTick(
                StreamId: "slack:reactions",
                EventType: "slack.reaction_added",
                Payload: "ops|beau|🎉|rx-2",
                OwnerContext: context),
            ct);

        var ingressReading = await WaitForJournalAsync(
            ingressId,
            reading => reading.AllHeard<ExternalStreamTick>().Count == 2
                && reading.AllSaid<SlackReactionAdded>().Count == 2,
            "ingress adapter journals ExternalStreamTick then says SlackReactionAdded",
            ct);

        // Ingress is the first journal hop of the stream stand-in path.
        var secondTickHeard = ingressReading.AllHeard<ExternalStreamTick>()
            .OrderBy(h => h.Position)
            .Last();
        Assert.Equal("rx-2", Assert.IsType<ExternalStreamTick>(secondTickHeard.Body).Payload.Split('|')[^1]);

        var reactionSaid = ingressReading.AllSaid<SlackReactionAdded>()
            .OrderBy(s => s.Position)
            .Last();
        Assert.Equal(new SynapseRef(session.Id, secondTickHeard.Metadata.Sequence), reactionSaid.Cause);
        Assert.Equal("declared", reactionSaid.DeliveryTo(gratitudeId).Via);
        Assert.Equal("rx-2", Assert.IsType<SlackReactionAdded>(reactionSaid.Body).ReactionId);

        var gratitudeReading = await WaitForJournalAsync(
            gratitudeId,
            reading => reading.AllHeard<SlackReactionAdded>().Count == 2
                && reading.AllSaid<NoteCaptured>().Count == 2
                && reading.AllSaid<StreamWakeUiToast>().Count == 2,
            "GratitudeNotes woke and journaled NoteCaptured + toast",
            ct);

        var noteSaid = gratitudeReading.AllSaid<NoteCaptured>()
            .OrderBy(s => s.Position)
            .Last();
        Assert.Equal("note-rx-2", Assert.IsType<NoteCaptured>(noteSaid.Body).NoteId);
        Assert.Equal("declared", noteSaid.DeliveryTo(ledgerId).Via);

        var ledgerReading = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<NoteCaptured>().Count == 2
                && reading.AllHeard<StreamWakeUiToast>().Count == 2,
            "ledger heard notes + toasts",
            ct);
        var notes = ledgerReading.AllHeard<NoteCaptured>();
        Assert.Equal(gratitudeId, notes[^1].Metadata.Source);
    }
}
