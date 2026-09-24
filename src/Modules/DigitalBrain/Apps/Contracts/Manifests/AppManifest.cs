namespace DigitalBrain.Apps;

// The app.json model: the manifest is the app.
[GenerateSerializer, Alias("apps.manifest")]
public sealed record AppManifest
{
    [Id(0)] public required string Id { get; init; }
    [Id(1)] public required string Version { get; init; }
    [Id(2)] public required string Publisher { get; init; }
    [Id(3)] public required AppKind Kind { get; init; }
    [Id(4)] public required string Name { get; init; }
    [Id(5)] public required string DescriptionForPeople { get; init; }
    [Id(6)] public required string DescriptionForModel { get; init; }
    [Id(7)] public IReadOnlyList<AppOperation> Operations { get; init; } = [];
    [Id(8)] public string? UiEntry { get; init; }
    [Id(9)] public IReadOnlyList<AppPermission> Permissions { get; init; } = [];
    [Id(10)] public IReadOnlyList<AppMeter> Meters { get; init; } = [];
    [Id(11)] public IReadOnlyList<string> ExamplePrompts { get; init; } = [];
    [Id(12)] public IReadOnlyList<AppScenario> Scenarios { get; init; } = [];
}
