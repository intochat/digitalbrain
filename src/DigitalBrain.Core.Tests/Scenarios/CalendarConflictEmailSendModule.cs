namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record MeetingScheduleAsked(
    string Title,
    string StartUtc,
    string EndUtc,
    string Attendee) : Synapse;

public sealed record CalendarConflictDetected(
    string RequestedTitle,
    string ConflictingEventId,
    string OverlapUtc) : Synapse;

public sealed record ConflictResolutionsProposed(
    string RequestedTitle,
    IReadOnlyList<string> Options) : Synapse;

public sealed record ConflictResolutionChosen(
    string RequestedTitle,
    string Choice) : Synapse;

public sealed record CalendarRescheduleProposed(
    string EventId,
    string NewStartUtc,
    string Reason) : Synapse;

public sealed record DeclineEmailDrafted(
    string To,
    string Subject,
    string Body,
    string RelatedTitle) : Synapse;

public sealed record DeclineEmailSent(
    string To,
    string Subject,
    string MessageId) : Synapse;

public sealed record MeetingScheduleCompleted(
    string Title,
    string Resolution) : Synapse;

// Detects busy slot, proposes Reschedule | DeclineEmail; applies choice on deferred turn.
public sealed class ConflictCalendar : Neuron<ConflictCalendarState>,
    INeuron<MeetingScheduleAsked>,
    INeuron<ConflictResolutionChosen>
{
    public const string OptionReschedule = "Reschedule";
    public const string OptionDeclineEmail = "DeclineEmail";
    public const string BusyEventId = "internal-standup";

    public Task HandleAsync(MeetingScheduleAsked fact, CancellationToken cancellationToken)
    {
        State.PendingTitle = fact.Title;
        State.PendingAttendee = fact.Attendee;
        State.PendingStartUtc = fact.StartUtc;
        State.Resolved = false;

        Emit(new CalendarConflictDetected(
            RequestedTitle: fact.Title,
            ConflictingEventId: BusyEventId,
            OverlapUtc: fact.StartUtc));
        Emit(new ConflictResolutionsProposed(
            fact.Title,
            Options: [OptionReschedule, OptionDeclineEmail]));
        return Task.CompletedTask;
    }

    public Task HandleAsync(ConflictResolutionChosen fact, CancellationToken cancellationToken)
    {
        if (State.Resolved
            || State.PendingTitle is null
            || !string.Equals(fact.RequestedTitle, State.PendingTitle, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        if (string.Equals(fact.Choice, OptionReschedule, StringComparison.Ordinal))
        {
            Emit(new CalendarRescheduleProposed(
                EventId: BusyEventId,
                NewStartUtc: "shifted-30m",
                Reason: $"Clear slot for {fact.RequestedTitle}"));
            State.Resolved = true;
            Emit(new MeetingScheduleCompleted(fact.RequestedTitle, OptionReschedule));
            return Task.CompletedTask;
        }

        if (string.Equals(fact.Choice, OptionDeclineEmail, StringComparison.Ordinal))
        {
            Emit(new DeclineEmailDrafted(
                To: State.PendingAttendee ?? "unknown",
                Subject: $"Decline: {fact.RequestedTitle}",
                Body: $"Cannot take {fact.RequestedTitle} at {State.PendingStartUtc}; conflicts with {BusyEventId}.",
                RelatedTitle: fact.RequestedTitle));
            State.Resolved = true;
            Emit(new MeetingScheduleCompleted(fact.RequestedTitle, OptionDeclineEmail));
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }
}

public sealed class ConflictCalendarState
{
    public string? PendingTitle { get; set; }
    public string? PendingAttendee { get; set; }
    public string? PendingStartUtc { get; set; }
    public bool Resolved { get; set; }
}

// Mock mail: draft ambient → sent fact (no network).
public sealed class ConflictDeclineMailer : Neuron, INeuron<DeclineEmailDrafted>
{
    public Task HandleAsync(DeclineEmailDrafted fact, CancellationToken cancellationToken)
    {
        Emit(new DeclineEmailSent(
            fact.To,
            fact.Subject,
            MessageId: $"decline-{fact.RelatedTitle.GetHashCode(StringComparison.Ordinal):x8}"));
        return Task.CompletedTask;
    }
}

// Catalog sinks so ambient conflict / proposal / draft / sent / completed Emits are legal.
public sealed class ConflictSurfaceLedger : Neuron,
    INeuron<CalendarConflictDetected>,
    INeuron<ConflictResolutionsProposed>,
    INeuron<CalendarRescheduleProposed>,
    INeuron<MeetingScheduleCompleted>
{
    public Task HandleAsync(CalendarConflictDetected fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(ConflictResolutionsProposed fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(CalendarRescheduleProposed fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(MeetingScheduleCompleted fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class DeclineEmailSentLedger : Neuron, INeuron<DeclineEmailSent>
{
    public Task HandleAsync(DeclineEmailSent fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
