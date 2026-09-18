namespace DigitalBrain.Core;

public sealed class NeuronOptions
{
    public const string SectionName = "DigitalBrain:Neuron";

    // Orleans' minimum reminder period; the grain timer, not the reminder, is the fast path.
    public TimeSpan RetryReminderPeriod { get; set; } = TimeSpan.FromMinutes(1);
}
