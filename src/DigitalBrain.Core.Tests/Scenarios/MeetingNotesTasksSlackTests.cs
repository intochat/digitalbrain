using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class MeetingNotesTasksSlackTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<MeetingNotesExtractor>()
            .AddModule<NotesTaskStore>()
            .AddModule<MockSlackPoster>()
            .AddModule<SlackPostLedger>();

    [Fact(DisplayName =
        "Meeting notes: MeetingNotesReady → NotesTaskCreated fan-out + SlackPostRequested → SlackMessagePosted (Cause chain)")]
    public async Task NotesReadyFansTasksAndSlackPost()
    {
        var ct = Cancellation;
        var context = "post-meeting";
        var session = Brain.Session(context);
        var extractorId = new NeuronId("meetingnotesextractor", context);
        var taskStoreId = new NeuronId("notestaskstore", context);
        var slackId = new NeuronId("mockslackposter", context);
        var slackLedgerId = new NeuronId("slackpostledger", context);
        var meetingId = "acme-q3";
        var channel = "#deals";

        await session.EmitAsync(
            new MeetingNotesReady(meetingId, "Acme wants proposal + deep-dive next week.", channel),
            ct);

        var extractorReading = await WaitForJournalAsync(
            extractorId,
            reading => reading.AllSaid<NotesTaskCreated>().Count == 2
                && reading.AllSaid<SlackPostRequested>().Count == 1,
            "two NotesTaskCreated and one SlackPostRequested",
            ct);

        var taskStoreReading = await WaitForJournalAsync(
            taskStoreId,
            reading => reading.AllHeard<NotesTaskCreated>().Count == 2,
            "NotesTaskStore heard both tasks",
            ct);

        var slackReading = await WaitForJournalAsync(
            slackId,
            reading => reading.AllSaid<SlackMessagePosted>().Count == 1,
            "SlackMessagePosted",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var notesSaid = sessionReading.SaidSingle<MeetingNotesReady>();
        Assert.Null(notesSaid.Cause);
        Assert.Equal("declared", notesSaid.DeliveryTo(extractorId).Via);

        var notesHeard = extractorReading.HeardSingle<MeetingNotesReady>();
        Assert.Equal(session.Id, notesHeard.Metadata.Source);
        Assert.Equal(notesSaid.Position, notesHeard.Metadata.Sequence);

        var tasksSaid = extractorReading.AllSaid<NotesTaskCreated>();
        Assert.Equal(2, tasksSaid.Count);
        Assert.All(tasksSaid, said =>
        {
            Assert.Equal(new SynapseRef(session.Id, notesSaid.Position), said.Cause);
            Assert.Equal("declared", said.DeliveryTo(taskStoreId).Via);
            Assert.Equal(meetingId, Assert.IsType<NotesTaskCreated>(said.Body).MeetingId);
        });
        var taskTitles = tasksSaid
            .Select(said => Assert.IsType<NotesTaskCreated>(said.Body).Title)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["Schedule technical deep-dive", "Send proposal to Acme"], taskTitles);

        var slackReqSaid = extractorReading.SaidSingle<SlackPostRequested>();
        Assert.Equal(new SynapseRef(session.Id, notesSaid.Position), slackReqSaid.Cause);
        Assert.Equal("declared", slackReqSaid.DeliveryTo(slackId).Via);
        var slackReq = Assert.IsType<SlackPostRequested>(slackReqSaid.Body);
        Assert.Equal(meetingId, slackReq.MeetingId);
        Assert.Equal(channel, slackReq.Channel);
        Assert.Contains("2 tasks", slackReq.Text, StringComparison.Ordinal);

        // Cause chain: session notes → extractor tasks/slack-req → slack poster posted.
        Assert.Equal(session.Id, slackReqSaid.Cause!.Value.Source);
        Assert.Equal(notesSaid.Position, slackReqSaid.Cause.Value.Sequence);

        var slackReqHeard = slackReading.HeardSingle<SlackPostRequested>();
        Assert.Equal(extractorId, slackReqHeard.Metadata.Source);
        Assert.Equal(slackReqSaid.Position, slackReqHeard.Metadata.Sequence);

        var postedSaid = slackReading.SaidSingle<SlackMessagePosted>();
        Assert.Equal(new SynapseRef(extractorId, slackReqSaid.Position), postedSaid.Cause);
        Assert.Equal("declared", postedSaid.DeliveryTo(slackLedgerId).Via);
        var posted = Assert.IsType<SlackMessagePosted>(postedSaid.Body);
        Assert.Equal(meetingId, posted.MeetingId);
        Assert.Equal(channel, posted.Channel);
        Assert.Contains(meetingId, posted.Permalink, StringComparison.Ordinal);

        foreach (var said in tasksSaid)
        {
            var heard = taskStoreReading.AllHeard<NotesTaskCreated>()
                .Single(entry => entry.Metadata.Sequence == said.Position);
            Assert.Equal(extractorId, heard.Metadata.Source);
            Assert.Equal(said.Position, heard.Metadata.Sequence);
        }

        var slackLedger = await WaitForJournalAsync(
            slackLedgerId,
            reading => reading.AllHeard<SlackMessagePosted>().Count == 1,
            "ledger heard SlackMessagePosted",
            ct);
        Assert.Equal(slackId, slackLedger.HeardSingle<SlackMessagePosted>().Metadata.Source);
        Assert.Equal(postedSaid.Position, slackLedger.HeardSingle<SlackMessagePosted>().Metadata.Sequence);

        // One causal thread from notes root through slack post.
        Assert.Equal(extractorId, postedSaid.Cause!.Value.Source);
        Assert.Equal(slackReqSaid.Position, postedSaid.Cause.Value.Sequence);
    }
}
