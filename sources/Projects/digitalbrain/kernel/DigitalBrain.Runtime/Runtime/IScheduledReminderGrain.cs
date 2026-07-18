namespace DigitalBrain.Runtime.Runtime;

public interface IScheduledReminderGrain : IGrainWithStringKey
{
    Task ScheduleReminderAsync(string reminderFqn, TimeSpan delay, IReadOnlyDictionary<string, string> payload);
    Task TriggerReceiveReminderAsync(string reminderName);
}
