namespace Ino.Domains.Reminders.Contracts;

/// <summary>
/// Journaled when the user successfully schedules a reminder. <see cref="Name"/>
/// is the unique key (also the IAW <c>ScheduledJobItem.Name</c>) — so a later
/// <see cref="ReminderCancelled"/> or <see cref="ReminderDue"/> can be linked
/// back to this set event by name.
/// </summary>
[GenerateSerializer]
public sealed record ReminderSet(
    [property: Id(0)] string Name,
    [property: Id(1)] string Description,
    [property: Id(2)] DateTimeOffset DueAt) : ReminderEvent;
