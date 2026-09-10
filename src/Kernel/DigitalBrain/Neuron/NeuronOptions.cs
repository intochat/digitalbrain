namespace DigitalBrain.Core;

public sealed class NeuronOptions
{
    // Orleans' minimum reminder period; the grain timer, not the reminder, is the fast path.
    public TimeSpan RetryReminderPeriod { get; init; } = TimeSpan.FromMinutes(1);
}
