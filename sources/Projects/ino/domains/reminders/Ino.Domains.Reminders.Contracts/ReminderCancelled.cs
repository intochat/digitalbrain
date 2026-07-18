namespace Ino.Domains.Reminders.Contracts;

/// <summary>
/// Journaled when the user (or another neuron) cancels a previously-scheduled
/// reminder. Cancellation also removes the IAW <c>ScheduledJobItem</c>; the
/// journal entry is the audit trail.
/// </summary>
[GenerateSerializer]
public sealed record ReminderCancelled(
    [property: Id(0)] string Name) : ReminderEvent;
