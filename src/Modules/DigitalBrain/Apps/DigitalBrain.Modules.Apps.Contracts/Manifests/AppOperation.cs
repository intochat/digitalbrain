namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.operation")]
public sealed record AppOperation
{
    [Id(0)] public required string Name { get; init; }
    [Id(1)] public required string DescriptionForModel { get; init; }
    [Id(2)] public bool ReadOnly { get; init; }
    [Id(3)] public IReadOnlyDictionary<string, string> InputTypeIds { get; init; } = new Dictionary<string, string>();
    [Id(4)] public string? OutputTypeId { get; init; }
}
