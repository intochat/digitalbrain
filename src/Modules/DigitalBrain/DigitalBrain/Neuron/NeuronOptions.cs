namespace DigitalBrain.Core;

public sealed class NeuronOptions
{
    // Azure client retry settings are not an aggregate deadline, so the journal storage decorator supplies one.
    public TimeSpan StorageOperationBudget { get; init; } = TimeSpan.FromSeconds(120);

    // Orleans' minimum reminder period; the grain timer, not the reminder, is the fast path.
    public TimeSpan RetryReminderPeriod { get; init; } = TimeSpan.FromMinutes(1);
}
