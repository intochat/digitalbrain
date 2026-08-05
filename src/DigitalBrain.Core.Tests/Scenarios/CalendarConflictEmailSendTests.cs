using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class CalendarConflictEmailSendTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<ConflictCalendar>()
            .AddModule<ConflictDeclineMailer>()
            .AddModule<ConflictSurfaceLedger>()
            .AddModule<DeclineEmailSentLedger>();

    [Fact(DisplayName =
        "Calendar conflict: detect busy slot → propose Reschedule|DeclineEmail → DeclineEmail draft/sent journaled")]
    public async Task ConflictThenDeclineEmailDraftAndSent()
    {
        var ct = Cancellation;
        var context = "owner-calendar";
        var session = Brain.Session(context);
        var calendarId = new NeuronId("conflictcalendar", context);
        var mailerId = new NeuronId("conflictdeclinemailer", context);
        var surfaceId = new NeuronId("conflictsurfaceledger", context);
        var sentLedgerId = new NeuronId("declineemailsentledger", context);
        var title = "Acme call";
        var attendee = "ceo@acme.example";
        var startUtc = "2026-08-07T14:00:00Z";

        await session.EmitAsync(
            new MeetingScheduleAsked(title, startUtc, "2026-08-07T15:00:00Z", attendee),
            ct);

        var calendarAfterConflict = await WaitForJournalAsync(
            calendarId,
            reading => reading.AllSaid<CalendarConflictDetected>().Count == 1
                && reading.AllSaid<ConflictResolutionsProposed>().Count == 1,
            "CalendarConflictDetected and ConflictResolutionsProposed",
            ct);

        Assert.Empty(calendarAfterConflict.AllSaid<DeclineEmailDrafted>());
        Assert.Empty(calendarAfterConflict.AllSaid<MeetingScheduleCompleted>());

        var sessionAfterAsk = await ReadAsync(session.Id, ct);
        var askSaid = sessionAfterAsk.SaidSingle<MeetingScheduleAsked>();
        Assert.Equal("declared", askSaid.DeliveryTo(calendarId).Via);

        var askHeard = calendarAfterConflict.HeardSingle<MeetingScheduleAsked>();
        Assert.Equal(session.Id, askHeard.Metadata.Source);
        Assert.Equal(askSaid.Position, askHeard.Metadata.Sequence);

        var conflictSaid = calendarAfterConflict.SaidSingle<CalendarConflictDetected>();
        Assert.Equal(new SynapseRef(session.Id, askSaid.Position), conflictSaid.Cause);
        Assert.Equal("declared", conflictSaid.DeliveryTo(surfaceId).Via);
        var conflict = Assert.IsType<CalendarConflictDetected>(conflictSaid.Body);
        Assert.Equal(title, conflict.RequestedTitle);
        Assert.Equal(ConflictCalendar.BusyEventId, conflict.ConflictingEventId);
        Assert.Equal(startUtc, conflict.OverlapUtc);

        var proposedSaid = calendarAfterConflict.SaidSingle<ConflictResolutionsProposed>();
        Assert.Equal(new SynapseRef(session.Id, askSaid.Position), proposedSaid.Cause);
        Assert.Equal("declared", proposedSaid.DeliveryTo(surfaceId).Via);
        var proposed = Assert.IsType<ConflictResolutionsProposed>(proposedSaid.Body);
        Assert.Equal(
            [ConflictCalendar.OptionReschedule, ConflictCalendar.OptionDeclineEmail],
            proposed.Options);

        var surfaceAfterConflict = await WaitForJournalAsync(
            surfaceId,
            reading => reading.AllHeard<CalendarConflictDetected>().Count == 1
                && reading.AllHeard<ConflictResolutionsProposed>().Count == 1,
            "surface ledger heard conflict + options",
            ct);
        Assert.Equal(calendarId, surfaceAfterConflict.HeardSingle<CalendarConflictDetected>().Metadata.Source);
        Assert.Equal(conflictSaid.Position, surfaceAfterConflict.HeardSingle<CalendarConflictDetected>().Metadata.Sequence);

        await session.EmitAsync(
            new ConflictResolutionChosen(title, ConflictCalendar.OptionDeclineEmail),
            ct);

        var calendarDone = await WaitForJournalAsync(
            calendarId,
            reading => reading.AllSaid<DeclineEmailDrafted>().Count == 1
                && reading.AllSaid<MeetingScheduleCompleted>().Count == 1,
            "DeclineEmailDrafted and MeetingScheduleCompleted after choice",
            ct);

        var mailerReading = await WaitForJournalAsync(
            mailerId,
            reading => reading.AllSaid<DeclineEmailSent>().Count == 1,
            "DeclineEmailSent",
            ct);

        var sessionAfterChoice = await ReadAsync(session.Id, ct);
        var choiceSaid = sessionAfterChoice.SaidSingle<ConflictResolutionChosen>();
        Assert.Equal("declared", choiceSaid.DeliveryTo(calendarId).Via);
        Assert.Equal(ConflictCalendar.OptionDeclineEmail, Assert.IsType<ConflictResolutionChosen>(choiceSaid.Body).Choice);

        var choiceHeard = calendarDone.HeardSingle<ConflictResolutionChosen>();
        Assert.Equal(session.Id, choiceHeard.Metadata.Source);
        Assert.Equal(choiceSaid.Position, choiceHeard.Metadata.Sequence);

        var draftSaid = calendarDone.SaidSingle<DeclineEmailDrafted>();
        Assert.Equal(new SynapseRef(session.Id, choiceSaid.Position), draftSaid.Cause);
        Assert.Equal("declared", draftSaid.DeliveryTo(mailerId).Via);
        var draft = Assert.IsType<DeclineEmailDrafted>(draftSaid.Body);
        Assert.Equal(attendee, draft.To);
        Assert.Equal($"Decline: {title}", draft.Subject);
        Assert.Contains(ConflictCalendar.BusyEventId, draft.Body, StringComparison.Ordinal);
        Assert.Equal(title, draft.RelatedTitle);

        var completedSaid = calendarDone.SaidSingle<MeetingScheduleCompleted>();
        Assert.Equal(new SynapseRef(session.Id, choiceSaid.Position), completedSaid.Cause);
        Assert.Equal("declared", completedSaid.DeliveryTo(surfaceId).Via);
        Assert.Equal(ConflictCalendar.OptionDeclineEmail, Assert.IsType<MeetingScheduleCompleted>(completedSaid.Body).Resolution);

        var draftHeard = mailerReading.HeardSingle<DeclineEmailDrafted>();
        Assert.Equal(calendarId, draftHeard.Metadata.Source);
        Assert.Equal(draftSaid.Position, draftHeard.Metadata.Sequence);

        var sentSaid = mailerReading.SaidSingle<DeclineEmailSent>();
        Assert.Equal(new SynapseRef(calendarId, draftSaid.Position), sentSaid.Cause);
        Assert.Equal("declared", sentSaid.DeliveryTo(sentLedgerId).Via);
        var sent = Assert.IsType<DeclineEmailSent>(sentSaid.Body);
        Assert.Equal(attendee, sent.To);
        Assert.Equal(draft.Subject, sent.Subject);
        Assert.False(string.IsNullOrWhiteSpace(sent.MessageId));

        var sentLedger = await WaitForJournalAsync(
            sentLedgerId,
            reading => reading.AllHeard<DeclineEmailSent>().Count == 1,
            "sent ledger heard DeclineEmailSent",
            ct);
        Assert.Equal(mailerId, sentLedger.HeardSingle<DeclineEmailSent>().Metadata.Source);
        Assert.Equal(sentSaid.Position, sentLedger.HeardSingle<DeclineEmailSent>().Metadata.Sequence);

        Assert.Empty(calendarDone.AllSaid<CalendarRescheduleProposed>());
    }

    [Fact(DisplayName =
        "Calendar conflict: owner chooses Reschedule → CalendarRescheduleProposed journaled (no decline mail)")]
    public async Task ConflictThenRescheduleProposed()
    {
        var ct = Cancellation;
        var context = "owner-calendar-reschedule";
        var session = Brain.Session(context);
        var calendarId = new NeuronId("conflictcalendar", context);
        var surfaceId = new NeuronId("conflictsurfaceledger", context);
        var title = "Board prep";

        await session.EmitAsync(
            new MeetingScheduleAsked(title, "2026-08-08T10:00:00Z", "2026-08-08T11:00:00Z", "board@co.example"),
            ct);

        await WaitForJournalAsync(
            calendarId,
            reading => reading.AllSaid<CalendarConflictDetected>().Count == 1,
            "conflict detected",
            ct);

        await session.EmitAsync(
            new ConflictResolutionChosen(title, ConflictCalendar.OptionReschedule),
            ct);

        var calendarDone = await WaitForJournalAsync(
            calendarId,
            reading => reading.AllSaid<CalendarRescheduleProposed>().Count == 1
                && reading.AllSaid<MeetingScheduleCompleted>().Count == 1,
            "reschedule proposed and schedule completed",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var choiceSaid = sessionReading.SaidSingle<ConflictResolutionChosen>();

        var rescheduleSaid = calendarDone.SaidSingle<CalendarRescheduleProposed>();
        Assert.Equal(new SynapseRef(session.Id, choiceSaid.Position), rescheduleSaid.Cause);
        Assert.Equal("declared", rescheduleSaid.DeliveryTo(surfaceId).Via);
        var reschedule = Assert.IsType<CalendarRescheduleProposed>(rescheduleSaid.Body);
        Assert.Equal(ConflictCalendar.BusyEventId, reschedule.EventId);
        Assert.Equal("shifted-30m", reschedule.NewStartUtc);

        Assert.Empty(calendarDone.AllSaid<DeclineEmailDrafted>());

        var surface = await WaitForJournalAsync(
            surfaceId,
            reading => reading.AllHeard<CalendarRescheduleProposed>().Count == 1,
            "surface heard reschedule",
            ct);
        Assert.Equal(calendarId, surface.HeardSingle<CalendarRescheduleProposed>().Metadata.Source);
        Assert.Equal(rescheduleSaid.Position, surface.HeardSingle<CalendarRescheduleProposed>().Metadata.Sequence);
    }
}
