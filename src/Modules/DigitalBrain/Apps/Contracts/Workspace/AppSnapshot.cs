namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.runtime-snapshot")]
public sealed record AppSnapshot
{
    [Id(0)] public required AppManifest Manifest { get; init; }
    [Id(1)] public IReadOnlyDictionary<string, string> Configuration { get; init; } = new Dictionary<string, string>();
    [Id(2)] public AppStatus Status { get; init; }
    [Id(3)] public long Revision { get; init; }
    [Id(4)] public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
    [Id(5)] public string Output { get; init; } = "";
}
