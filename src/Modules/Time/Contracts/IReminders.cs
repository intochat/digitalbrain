using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Time;

[Alias("reminders")]
public interface IReminders : INeuron
{
    [Alias("schedule")]
    Task<Accepted<ReminderKey>> Schedule(SetReminder command);

    [Alias("cancel")]
    Task<Accepted<ReminderKey>> Cancel(CancelReminder command);

    [ReadOnly, Alias("read")]
    Task<RemindersSnapshot> Read();
}

[GenerateSerializer, Alias("time.set-reminder")]
public sealed record SetReminder(CommandId Id,
    [property: Id(0)] string ReminderId,
    [property: Id(1)] string Text,
    [property: Id(2)] long DueUnixSeconds) : Command(Id);

[GenerateSerializer, Alias("time.cancel-reminder")]
public sealed record CancelReminder(CommandId Id, [property: Id(0)] string ReminderId) : Command(Id);

[GenerateSerializer, Alias("time.reminder-key")]
public sealed record ReminderKey([property: Id(0)] string ReminderId);

[GenerateSerializer, Alias("time.reminder-due")]
public sealed record ReminderDue(
    [property: Id(0)] string ReminderId,
    [property: Id(1)] string Text,
    [property: Id(2)] long DueUnixSeconds);

[GenerateSerializer, Alias("time.reminder-rejected")]
public sealed record ReminderRejected(
    [property: Id(0)] string ReminderId,
    [property: Id(1)] string Reason);

[GenerateSerializer, Alias("time.reminder-scheduling")]
public sealed record ReminderScheduling(
    [property: Id(0)] string ReminderId,
    [property: Id(1)] string Text,
    [property: Id(2)] long DueUnixSeconds,
    [property: Id(3)] long? RequestedAtUnixSeconds = null);

public enum ReminderStatus { Scheduled, Delivered, Cancelled }

[GenerateSerializer, Alias("time.reminder-item")]
public sealed record ReminderItem(
    [property: Id(0)] string ReminderId,
    [property: Id(1)] string Text,
    [property: Id(2)] long DueUnixSeconds,
    [property: Id(3)] ReminderStatus Status);

[GenerateSerializer, Alias("time.reminders-snapshot")]
public sealed record RemindersSnapshot([property: Id(0)] IReadOnlyList<ReminderItem> Items);
