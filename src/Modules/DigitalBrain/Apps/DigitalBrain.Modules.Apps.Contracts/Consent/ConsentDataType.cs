namespace DigitalBrain.Apps;

// One data class the consent sheet names: which semantic type the app touches and why.
[GenerateSerializer, Alias("apps.consent-data-type")]
public sealed record ConsentDataType
{
    [Id(0)] public required string SemanticTypeId { get; init; }
    [Id(1)] public required string Reason { get; init; }
    [Id(2)] public bool Write { get; init; }
}
