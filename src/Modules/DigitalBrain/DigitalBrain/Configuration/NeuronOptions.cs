namespace DigitalBrain.Core;

public sealed class NeuronOptions
{
    public const string SectionName = "DigitalBrain:Neuron";

    // Azure client retry settings are not an aggregate deadline, so the journal storage decorator supplies one.
    public TimeSpan StorageOperationBudget { get; set; } = TimeSpan.FromSeconds(120);

    // Orleans' minimum reminder period; the grain timer, not the reminder, is the fast path.
    public TimeSpan RetryReminderPeriod { get; set; } = TimeSpan.FromMinutes(1);
}
