using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class VoiceNoteTasksCalendarTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<MockVoiceTranscriber>()
            .AddModule<VoiceActionExtractor>()
            .AddModule<TaskStore>()
            .AddModule<CalendarBlockLedger>()
            .AddModule<VoiceTranscriptLedger>();

    [Fact(DisplayName =
        "Voice note: VoiceNoteReceived → VoiceTranscriptReady → TaskCreated×2 + CalendarBlockProposed (mock STT)")]
    public async Task VoiceNoteYieldsTasksAndCalendarBlock()
    {
        var ct = Cancellation;
        var context = "owner-mobile";
        var session = Brain.Session(context);
        var voiceId = new NeuronId("mockvoicetranscriber", context);
        var extractorId = new NeuronId("voiceactionextractor", context);
        var taskStoreId = new NeuronId("taskstore", context);
        var calendarLedgerId = new NeuronId("calendarblockledger", context);
        var transcriptLedgerId = new NeuronId("voicetranscriptledger", context);
        var blobRef = "blob-voice-91";

        await session.EmitAsync(new VoiceNoteReceived(blobRef, DurationSeconds: 90, DeviceId: "pixel-7"), ct);

        var extractorReading = await WaitForJournalAsync(
            extractorId,
            reading => reading.AllSaid<TaskCreated>().Count == 2
                && reading.AllSaid<CalendarBlockProposed>().Count == 1,
            "two TaskCreated and one CalendarBlockProposed",
            ct);

        var taskStoreReading = await WaitForJournalAsync(
            taskStoreId,
            reading => reading.AllHeard<TaskCreated>().Count == 2,
            "TaskStore heard both voice tasks",
            ct);

        var calendarReading = await WaitForJournalAsync(
            calendarLedgerId,
            reading => reading.AllHeard<CalendarBlockProposed>().Count == 1,
            "calendar ledger heard CalendarBlockProposed",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var noteSaid = sessionReading.SaidSingle<VoiceNoteReceived>();
        Assert.Equal("declared", noteSaid.DeliveryTo(voiceId).Via);

        var voiceReading = await ReadAsync(voiceId, ct);
        var noteHeard = voiceReading.HeardSingle<VoiceNoteReceived>();
        Assert.Equal(session.Id, noteHeard.Metadata.Source);
        Assert.Equal(noteSaid.Position, noteHeard.Metadata.Sequence);

        var transcriptSaid = voiceReading.SaidSingle<VoiceTranscriptReady>();
        Assert.Equal(new SynapseRef(session.Id, noteSaid.Position), transcriptSaid.Cause);
        Assert.Equal("declared", transcriptSaid.DeliveryTo(extractorId).Via);
        Assert.Equal("declared", transcriptSaid.DeliveryTo(transcriptLedgerId).Via);
        var transcript = Assert.IsType<VoiceTranscriptReady>(transcriptSaid.Body);
        Assert.Equal(blobRef, transcript.BlobRef);
        Assert.Contains("Priya", transcript.Text, StringComparison.Ordinal);
        Assert.True(transcript.Confidence >= 0.5);

        var transcriptHeard = extractorReading.HeardSingle<VoiceTranscriptReady>();
        Assert.Equal(voiceId, transcriptHeard.Metadata.Source);
        Assert.Equal(transcriptSaid.Position, transcriptHeard.Metadata.Sequence);

        var tasksSaid = extractorReading.AllSaid<TaskCreated>();
        Assert.Equal(2, tasksSaid.Count);
        Assert.All(tasksSaid, said =>
        {
            Assert.Equal(new SynapseRef(voiceId, transcriptSaid.Position), said.Cause);
            Assert.Equal("declared", said.DeliveryTo(taskStoreId).Via);
            var body = Assert.IsType<TaskCreated>(said.Body);
            Assert.Equal(blobRef, body.SourceMessageId);
            Assert.Equal("voice", body.Tag);
        });
        var titles = tasksSaid
            .Select(said => Assert.IsType<TaskCreated>(said.Body).Title)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["Call Priya about the contract", "Send the redlines today"], titles);

        var blockSaid = extractorReading.SaidSingle<CalendarBlockProposed>();
        Assert.Equal(new SynapseRef(voiceId, transcriptSaid.Position), blockSaid.Cause);
        Assert.Equal("declared", blockSaid.DeliveryTo(calendarLedgerId).Via);
        var block = Assert.IsType<CalendarBlockProposed>(blockSaid.Body);
        Assert.Equal($"block-board-{blobRef}", block.BlockId);
        Assert.Equal("Board deck focus", block.Title);
        Assert.Equal("2h", block.DurationHint);
        Assert.Equal(blobRef, block.SourceBlobRef);

        foreach (var said in tasksSaid)
        {
            var heard = taskStoreReading.AllHeard<TaskCreated>()
                .Single(entry => entry.Metadata.Sequence == said.Position);
            Assert.Equal(extractorId, heard.Metadata.Source);
            Assert.Equal(said.Position, heard.Metadata.Sequence);
        }

        Assert.Equal(extractorId, calendarReading.HeardSingle<CalendarBlockProposed>().Metadata.Source);
        Assert.Equal(blockSaid.Position, calendarReading.HeardSingle<CalendarBlockProposed>().Metadata.Sequence);
    }
}
