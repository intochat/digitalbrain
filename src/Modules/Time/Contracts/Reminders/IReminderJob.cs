namespace DigitalBrain.Time.Reminders;

// A durable job callback: a ReminderNeuron ticked with the "job" registration calls the grain
// that shares its key. The grain type matches IAutomation, the only durable job host today.
[Alias("reminder-job")]
[Orleans.Metadata.DefaultGrainType("automation")]
public interface IReminderJob : IGrainWithStringKey
{
    Task RunJob(DateTimeOffset scheduledFor);
}
