namespace DigitalBrain.Apps;

public enum AppKind
{
    Declarative = 0,
    Remote = 1,
    Process = 2,
}

[GenerateSerializer, Alias("apps.operation")]
public sealed record AppOperation
{
    [Id(0)] public required string Name { get; init; }
    [Id(1)] public required string DescriptionForModel { get; init; }
    [Id(2)] public bool ReadOnly { get; init; }
    [Id(3)] public IReadOnlyDictionary<string, string> InputTypeIds { get; init; } = new Dictionary<string, string>();
    [Id(4)] public string? OutputTypeId { get; init; }
}

[GenerateSerializer, Alias("apps.meter")]
public sealed record AppMeter
{
    [Id(0)] public required string MeterId { get; init; }
    [Id(1)] public required string Unit { get; init; }
    [Id(2)] public required string Aggregation { get; init; }
    [Id(3)] public decimal? ProposedPriceInCompute { get; init; }
}

[GenerateSerializer, Alias("apps.permission")]
public sealed record AppPermission
{
    [Id(0)] public required string SemanticTypeId { get; init; }
    [Id(1)] public required string Reason { get; init; }
    [Id(2)] public bool Write { get; init; }
}

[GenerateSerializer, Alias("apps.scenario")]
public sealed record AppScenario
{
    [Id(0)] public required string Name { get; init; }
    [Id(1)] public required string Given { get; init; }
    [Id(2)] public required string When { get; init; }
    [Id(3)] public required string Then { get; init; }
}

// A workspace window a saved declarative app reopens: kind 'table' resolves through the brain,
// any other kind is a surface inside the owning workspace.
[GenerateSerializer, Alias("apps.window")]
public sealed record AppWindow
{
    [Id(0)] public required string WindowId { get; init; }
    [Id(1)] public required string Title { get; init; }
    [Id(2)] public required string Kind { get; init; }
    [Id(3)] public required string NeuronId { get; init; }
}

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
    [Id(13)] public string? RemoteEndpoint { get; init; }
    [Id(14)] public IReadOnlyList<AppWindow> Windows { get; init; } = [];
}
