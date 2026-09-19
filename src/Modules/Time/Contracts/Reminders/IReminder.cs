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
}
