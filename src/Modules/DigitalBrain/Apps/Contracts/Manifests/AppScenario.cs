namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.scenario")]
public sealed record AppScenario
{
    [Id(0)] public required string Name { get; init; }
    [Id(1)] public required string Given { get; init; }
    [Id(2)] public required string When { get; init; }
    [Id(3)] public required string Then { get; init; }
}
