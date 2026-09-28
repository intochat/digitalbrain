using DigitalBrain.Apps;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

internal sealed record AppManifestSeed
{
    public string? Id { get; init; }
    public required string Version { get; init; }
    public required string Publisher { get; init; }
    public required AppKind Kind { get; init; }
    public string? Name { get; init; }
    public required string DescriptionForPeople { get; init; }
    public required string DescriptionForModel { get; init; }
    public string? UiEntry { get; init; }
    public IReadOnlyList<AppPermission> Permissions { get; init; } = [];
    public IReadOnlyList<AppMeter> Meters { get; init; } = [];
    public IReadOnlyList<string> ExamplePrompts { get; init; } = [];
    public IReadOnlyList<AppScenario> Scenarios { get; init; } = [];
}
