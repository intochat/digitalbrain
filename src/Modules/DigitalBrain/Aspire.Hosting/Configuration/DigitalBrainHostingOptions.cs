namespace DigitalBrain.Aspire.Hosting;

public sealed class DigitalBrainHostingOptions
{
    public const string SectionName = "DigitalBrain:Hosting";

    // Omit to let each runtime host configure its own neuron settings.
    public TimeSpan? StorageOperationBudget { get; set; }
    public TimeSpan? RetryReminderPeriod { get; set; }
}
