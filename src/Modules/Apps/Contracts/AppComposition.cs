namespace DigitalBrain.Apps;

public enum AppActivation { OnDemand = 0, WithWorkspace = 1, Background = 2 }
public enum AppStatus { Installed = 0, Running = 1, Stopped = 2 }

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

[GenerateSerializer, Alias("apps.part")]
public sealed record AppPart(
    [property: Id(0)] string Name,
    [property: Id(1)] string Behavior,
    [property: Id(2)] IReadOnlyDictionary<string, string> Settings);

[GenerateSerializer, Alias("apps.binding")]
public sealed record AppBinding(
    [property: Id(0)] string Source,
    [property: Id(1)] string Signal,
    [property: Id(2)] string Target);
