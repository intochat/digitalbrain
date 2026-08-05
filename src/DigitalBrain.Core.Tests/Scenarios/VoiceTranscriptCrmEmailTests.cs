using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class VoiceTranscriptCrmEmailTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<CallTranscriber>()
            .AddModule<CallCoach>()
            .AddModule<CallContactDirectory>()
            .AddModule<CallWorkflowLedger>();

    [Fact(DisplayName =
        "Voice transcript CRM email: CallEnded → CallTranscriptReady → CallSummarized + ContactResolved → CrmNoteLogged + FollowUpEmailDrafted")]
    public async Task CallChainWritesCrmNoteAndEmailDraft()
    {
        var ct = Cancellation;
        var context = "call-77";
        var session = Brain.Session(context);
        var transcriberId = new NeuronId("calltranscriber", context);
        var coachId = new NeuronId("callcoach", context);
        var directoryId = new NeuronId("callcontactdirectory", context);
        var ledgerId = new NeuronId("callworkflowledger", context);
        var callId = "call-77";

        await session.EmitAsync(
            new CallEnded(callId, RecordingRef: "rec-77", ContactHint: "Acme CFO"),
            ct);

        var coachReading = await WaitForJournalAsync(
            coachId,
            reading => reading.AllSaid<CrmNoteLogged>().Count == 1
                && reading.AllSaid<FollowUpEmailDrafted>().Count == 1
                && reading.AllSaid<CallSummarized>().Count == 1
                && reading.AllHeard<ContactResolved>().Count == 1,
            "coach completed CRM note + email draft after contact resolve",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var endedSaid = sessionReading.SaidSingle<CallEnded>();
        Assert.Equal("declared", endedSaid.DeliveryTo(transcriberId).Via);

        var transcriberReading = await ReadAsync(transcriberId, ct);
        var transcriptSaid = transcriberReading.SaidSingle<CallTranscriptReady>();
        Assert.Equal(new SynapseRef(session.Id, endedSaid.Position), transcriptSaid.Cause);
        Assert.Equal("declared", transcriptSaid.DeliveryTo(coachId).Via);
        Assert.Contains("Acme", Assert.IsType<CallTranscriptReady>(transcriptSaid.Body).Text, StringComparison.Ordinal);

        var resolveAsk = coachReading.SaidSingle<ResolveContactAsked>();
        Assert.Equal("ask", resolveAsk.DeliveryTo(directoryId).Via);

        var contactHeard = coachReading.HeardSingle<ContactResolved>();
        Assert.Equal(directoryId, contactHeard.Metadata.Source);
        Assert.Equal(new SynapseRef(coachId, resolveAsk.Position), contactHeard.Answers);
        Assert.Equal("crm-acme-1", Assert.IsType<ContactResolved>(contactHeard.Body).ContactId);

        var noteSaid = coachReading.SaidSingle<CrmNoteLogged>();
        Assert.Equal("declared", noteSaid.DeliveryTo(ledgerId).Via);
        Assert.Equal(callId, Assert.IsType<CrmNoteLogged>(noteSaid.Body).CallId);
        Assert.Equal("crm-acme-1", Assert.IsType<CrmNoteLogged>(noteSaid.Body).ContactId);

        var draftSaid = coachReading.SaidSingle<FollowUpEmailDrafted>();
        Assert.Equal("declared", draftSaid.DeliveryTo(ledgerId).Via);
        Assert.Equal("cfo@acme.example", Assert.IsType<FollowUpEmailDrafted>(draftSaid.Body).To);

        var summarizedSaid = coachReading.SaidSingle<CallSummarized>();
        Assert.Equal(new SynapseRef(transcriberId, transcriptSaid.Position), summarizedSaid.Cause);

        var ledgerReading = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<CrmNoteLogged>().Count == 1
                && reading.AllHeard<FollowUpEmailDrafted>().Count == 1
                && reading.AllHeard<CallSummarized>().Count == 1,
            "ledger heard CRM + draft + summary",
            ct);
        Assert.Equal(coachId, ledgerReading.HeardSingle<CrmNoteLogged>().Metadata.Source);
        Assert.Equal(noteSaid.Position, ledgerReading.HeardSingle<CrmNoteLogged>().Metadata.Sequence);
    }
}
