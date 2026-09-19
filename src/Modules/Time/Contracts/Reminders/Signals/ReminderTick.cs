using DigitalBrain.Contracts;
namespace DigitalBrain.Time.Reminders.Signals;

[GenerateSerializer, Alias("time.reminder-tick")]
public sealed record ReminderTick(
    [property: Id(0)] string ReminderId,
    [property: Id(1)] DateTimeOffset ObservedAt) : Signal;
