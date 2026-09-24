namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.proxy-outcome")]
public sealed record AppProxyOutcome
{
    [Id(0)] public bool Allowed { get; init; }
    [Id(1)] public string? Denial { get; init; }
    [Id(2)] public string? Explanation { get; init; }
}
