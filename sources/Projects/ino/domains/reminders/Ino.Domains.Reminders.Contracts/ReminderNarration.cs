using Ino.Core;

namespace Ino.Domains.Reminders.Contracts;

/// <summary>
/// User-facing fire when a reminder comes due. Broadcast by
/// <c>RemindersNeuron</c>'s scheduled-job handler so the gateway can stream
/// the message back through whichever surface the user is on (Flutter web,
/// Telegram, …). Distinct from <see cref="ReminderDue"/>: <see cref="ReminderDue"/>
/// is the journal entry, this is the side-effecting notification.
/// </summary>
[GenerateSerializer]
public sealed record ReminderNarration(
    [property: Id(0)] string Description,
    [property: Id(1)] string UserId) : ISynapse;
