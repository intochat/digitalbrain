using DigitalBrain.Coding;

namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.app-state")]
public sealed record AppState
{
    [Id(0)] public AppStatus Status { get; init; }
    [Id(1)] public PackageRevisionRef? Revision { get; init; }
    [Id(2)] public CodeArtifactRef? Artifact { get; init; }
    [Id(3)] public List<PackageSetting> Declared { get; init; } = [];
    [Id(4)] public Dictionary<string, string> Settings { get; init; } = [];
    [Id(5)] public List<PackageOperation> Operations { get; init; } = [];
    [Id(6)] public int Installation { get; init; }
    [Id(7)] public List<AppInvocation> Invocations { get; init; } = [];
    [Id(8)] public List<OperationReceipt> Receipts { get; init; } = [];
}
