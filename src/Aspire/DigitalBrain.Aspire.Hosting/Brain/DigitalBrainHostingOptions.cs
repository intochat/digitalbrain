namespace DigitalBrain.Aspire.Hosting;

public sealed class DigitalBrainHostingOptions
{
    public string? ServiceId { get; init; }
    public string? ClusterId { get; init; }
    public bool UseAzureStorage { get; init; }
    public bool Dashboard { get; init; }
}
