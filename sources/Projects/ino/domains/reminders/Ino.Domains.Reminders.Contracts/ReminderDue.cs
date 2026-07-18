namespace Ino.Domains.Reminders.Contracts;

/// <summary>
/// Journaled when a scheduled reminder fires. Emitted by
/// <c>RemindersNeuron.OnScheduledJobDueAsync</c> after IAW's
/// <see cref="Orleans.DurableJobs"/> runtime delivers the due tick.
/// </summary>
[GenerateSerializer]
public sealed record ReminderDue(
    [property: Id(0)] string Name,
    [property: Id(1)] string Description,
    [property: Id(2)] DateTimeOffset FiredAt) : ReminderEvent;
