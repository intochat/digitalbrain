namespace DigitalBrain.Apps;

/// <summary>A portable graph. Names are local to the installed app; no grain addresses are accepted.</summary>
[GenerateSerializer, Alias("apps.composition")]
public sealed record AppComposition
{
    [Id(0)] public IReadOnlyList<string> RequiredModules { get; init; } = [];
    [Id(1)] public AppActivation Activation { get; init; }
    [Id(2)] public IReadOnlyDictionary<string, string> Defaults { get; init; } = new Dictionary<string, string>();
    [Id(3)] public IReadOnlyList<AppPart> Parts { get; init; } = [];
    [Id(4)] public IReadOnlyList<AppBinding> Bindings { get; init; } = [];
}
