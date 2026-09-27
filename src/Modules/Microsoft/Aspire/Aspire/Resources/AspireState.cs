namespace DigitalBrain.Microsoft.Aspire;

[GenerateSerializer, Alias("microsoft.aspire.state")]
internal sealed record AspireState
{
    [Id(0)] public Dictionary<string, AspireResource> Resources { get; init; } = new(StringComparer.Ordinal);
}
