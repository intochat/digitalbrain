namespace DigitalBrain.Microsoft.Aspire;

public sealed class AspireOptions
{
    public const string SectionName = AspireModule.ConfigurationRoot;
    public string ApplicationName { get; set; } = "DigitalBrain";
    // The AppHost sets it at launch; without it the bridge endpoints answer 404.
    public string? BridgeKey { get; set; }
}
