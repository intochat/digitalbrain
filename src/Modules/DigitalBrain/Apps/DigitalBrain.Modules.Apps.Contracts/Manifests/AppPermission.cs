namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.permission")]
public sealed record AppPermission
{
    [Id(0)] public required string SemanticTypeId { get; init; }
    [Id(1)] public required string Reason { get; init; }
    [Id(2)] public bool Write { get; init; }
}
