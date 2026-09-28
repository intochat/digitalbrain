namespace DigitalBrain.Microsoft.GitHub;

[GenerateSerializer, Alias("github.refresh-repository")]
public sealed record RefreshRepository([property: Id(0)] int? Number = null);