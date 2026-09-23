using DigitalBrain.Contracts;
namespace DigitalBrain.Time.Reminders;

[Alias("reminder")]
[Orleans.Metadata.DefaultGrainType("reminder")]
public interface IReminder : INeuron
{
    /// <summary>Replace the persistent periodic registration. Missed ticks are not replayed.</summary>
    Task Start(TimeSpan dueTime, TimeSpan period);
    /// <summary>Remove the registration. A previously queued tick may still arrive.</summary>
    Task Stop();

    // The "job" registration drives durable work: each tick calls the grain that shares this
    // reminder's key through IReminderJob. Kept apart from the plain tick so existing reminder
    // consumers are unaffected.
    Task StartJob(TimeSpan dueTime, TimeSpan period);
    Task StopJob();
}