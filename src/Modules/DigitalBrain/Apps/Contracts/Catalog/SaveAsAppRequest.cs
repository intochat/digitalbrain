namespace DigitalBrain.Apps;

// A Save as app request turns the current window into a declarative app in this workspace.
[GenerateSerializer, Alias("apps.save-as-app")]
public sealed record SaveAsAppRequest
{
    [Id(0)] public required string Id { get; init; }
    [Id(1)] public required string Name { get; init; }
    [Id(2)] public required string DescriptionForPeople { get; init; }
    [Id(3)] public string? DescriptionForModel { get; init; }
    [Id(4)] public string? UiEntry { get; init; }
    [Id(5)] public IReadOnlyList<AppOperation> Operations { get; init; } = [];
    [Id(6)] public IReadOnlyList<string> ExamplePrompts { get; init; } = [];
    [Id(7)] public IReadOnlyList<AppPermission> Permissions { get; init; } = [];
    [Id(8)] public IReadOnlyList<AppMeter> Meters { get; init; } = [];
    [Id(9)] public IReadOnlyList<AppScenario> Scenarios { get; init; } = [];
    [Id(10)] public IReadOnlyList<AppWindow> Windows { get; init; } = [];
}
