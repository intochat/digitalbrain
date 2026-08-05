using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class MeetingTranscriptActionFanoutTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<MeetingActionExtractor>()
            .AddModule<ActionTasksHandler>()
            .AddModule<ActionEmailHandler>()
            .AddModule<ActionCrmHandler>()
            .AddModule<ActionLaneLedger>();

    [Fact(DisplayName =
        "Meeting transcript action fan-out: TranscriptReady → ActionItemCreated×3 to distinct handlers; shared Source/Sequence per item")]
    public async Task TranscriptFansActionItemsToThreeHandlers()
    {
        var ct = Cancellation;
        var context = "customer-call";
        var session = Brain.Session(context);
        var extractorId = new NeuronId("meetingactionextractor", context);
        var tasksId = new NeuronId("actiontaskshandler", context);
        var emailId = new NeuronId("actionemailhandler", context);
        var crmId = new NeuronId("actioncrmhandler", context);
        var ledgerId = new NeuronId("actionlaneledger", context);
        var meetingId = "zoom-441";

        await session.EmitAsync(
            new MeetingTranscriptReady(
                meetingId,
                TranscriptText: "Decided to ship proposal Friday; email legal; log next step in SF.",
                Attendees: ["owner", "acme-ceo"]),
            ct);

        var extractorReading = await WaitForJournalAsync(
            extractorId,
            reading => reading.AllSaid<ActionItemCreated>().Count == 3,
            "three ActionItemCreated",
            ct);

        var tasksReading = await WaitForJournalAsync(
            tasksId,
            reading => reading.AllHeard<ActionItemCreated>().Count == 3
                && reading.AllSaid<ActionLaneAcknowledged>().Count == 1,
            "tasks handler heard all ActionItemCreated and acked its lane",
            ct);

        var emailReading = await WaitForJournalAsync(
            emailId,
            reading => reading.AllHeard<ActionItemCreated>().Count == 3
                && reading.AllSaid<ActionLaneAcknowledged>().Count == 1,
            "email handler heard all ActionItemCreated and acked its lane",
            ct);

        var crmReading = await WaitForJournalAsync(
            crmId,
            reading => reading.AllHeard<ActionItemCreated>().Count == 3
                && reading.AllSaid<ActionLaneAcknowledged>().Count == 1,
            "crm handler heard all ActionItemCreated and acked its lane",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var transcriptSaid = sessionReading.SaidSingle<MeetingTranscriptReady>();
        Assert.Equal("declared", transcriptSaid.DeliveryTo(extractorId).Via);

        var transcriptHeard = extractorReading.HeardSingle<MeetingTranscriptReady>();
        Assert.Equal(session.Id, transcriptHeard.Metadata.Source);
        Assert.Equal(transcriptSaid.Position, transcriptHeard.Metadata.Sequence);

        var actionsSaid = extractorReading.AllSaid<ActionItemCreated>();
        Assert.Equal(3, actionsSaid.Count);
        Assert.All(actionsSaid, said =>
        {
            Assert.Equal(new SynapseRef(session.Id, transcriptSaid.Position), said.Cause);
            Assert.Equal("declared", said.DeliveryTo(tasksId).Via);
            Assert.Equal("declared", said.DeliveryTo(emailId).Via);
            Assert.Equal("declared", said.DeliveryTo(crmId).Via);
        });

        var lanes = actionsSaid
            .Select(said => Assert.IsType<ActionItemCreated>(said.Body).Lane)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [MeetingActionExtractor.LaneCrm, MeetingActionExtractor.LaneEmail, MeetingActionExtractor.LaneTasks],
            lanes);

        // Fan-out Source/Sequence: each ActionItemCreated is heard identically on all three handlers.
        foreach (var said in actionsSaid)
        {
            var tasksHeard = tasksReading.AllHeard<ActionItemCreated>()
                .Single(entry => entry.Metadata.Sequence == said.Position);
            var emailHeard = emailReading.AllHeard<ActionItemCreated>()
                .Single(entry => entry.Metadata.Sequence == said.Position);
            var crmHeard = crmReading.AllHeard<ActionItemCreated>()
                .Single(entry => entry.Metadata.Sequence == said.Position);

            Assert.Equal(extractorId, tasksHeard.Metadata.Source);
            Assert.Equal(extractorId, emailHeard.Metadata.Source);
            Assert.Equal(extractorId, crmHeard.Metadata.Source);
            Assert.Equal(said.Position, tasksHeard.Metadata.Sequence);
            Assert.Equal(said.Position, emailHeard.Metadata.Sequence);
            Assert.Equal(said.Position, crmHeard.Metadata.Sequence);
            Assert.Equal(tasksHeard.Metadata.Source, emailHeard.Metadata.Source);
            Assert.Equal(tasksHeard.Metadata.Sequence, emailHeard.Metadata.Sequence);
            Assert.Equal(tasksHeard.Metadata.Source, crmHeard.Metadata.Source);
            Assert.Equal(tasksHeard.Metadata.Sequence, crmHeard.Metadata.Sequence);

            var body = Assert.IsType<ActionItemCreated>(said.Body);
            Assert.Equal(body.ActionId, Assert.IsType<ActionItemCreated>(tasksHeard.Body).ActionId);
            Assert.Equal(body.Lane, Assert.IsType<ActionItemCreated>(emailHeard.Body).Lane);
            Assert.Equal(meetingId, Assert.IsType<ActionItemCreated>(crmHeard.Body).MeetingId);
        }

        var tasksAck = tasksReading.SaidSingle<ActionLaneAcknowledged>();
        Assert.Equal(MeetingActionExtractor.LaneTasks, Assert.IsType<ActionLaneAcknowledged>(tasksAck.Body).Lane);
        Assert.Equal("declared", tasksAck.DeliveryTo(ledgerId).Via);

        var emailAck = emailReading.SaidSingle<ActionLaneAcknowledged>();
        Assert.Equal(MeetingActionExtractor.LaneEmail, Assert.IsType<ActionLaneAcknowledged>(emailAck.Body).Lane);

        var crmAck = crmReading.SaidSingle<ActionLaneAcknowledged>();
        Assert.Equal(MeetingActionExtractor.LaneCrm, Assert.IsType<ActionLaneAcknowledged>(crmAck.Body).Lane);

        // Cause of each ack is the ActionItemCreated said row for that lane.
        var tasksActionSaid = actionsSaid.Single(said =>
            Assert.IsType<ActionItemCreated>(said.Body).Lane == MeetingActionExtractor.LaneTasks);
        var emailActionSaid = actionsSaid.Single(said =>
            Assert.IsType<ActionItemCreated>(said.Body).Lane == MeetingActionExtractor.LaneEmail);
        var crmActionSaid = actionsSaid.Single(said =>
            Assert.IsType<ActionItemCreated>(said.Body).Lane == MeetingActionExtractor.LaneCrm);

        Assert.Equal(new SynapseRef(extractorId, tasksActionSaid.Position), tasksAck.Cause);
        Assert.Equal(new SynapseRef(extractorId, emailActionSaid.Position), emailAck.Cause);
        Assert.Equal(new SynapseRef(extractorId, crmActionSaid.Position), crmAck.Cause);

        var ledgerReading = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<ActionLaneAcknowledged>().Count == 3,
            "ledger heard three lane acks",
            ct);
        Assert.Equal(3, ledgerReading.AllHeard<ActionLaneAcknowledged>().Count);
    }
}
