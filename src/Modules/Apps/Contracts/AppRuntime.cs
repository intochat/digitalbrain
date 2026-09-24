using DigitalBrain.Contracts;
using Orleans.Metadata;

namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.dispatch")]
public sealed record AppDispatch([property: Id(0)] Guid OperationId, [property: Id(1)] string Signal, [property: Id(2)] string Value);

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

[GenerateSerializer, Alias("apps.runtime-changed")]
public sealed record AppChanged([property: Id(0)] long Revision, [property: Id(1)] AppStatus Status) : Signal;

[GenerateSerializer, Alias("apps.runtime-signal")]
public sealed record AppSignal([property: Id(0)] string Source, [property: Id(1)] string Name, [property: Id(2)] string Value) : Signal;

[Alias("apps.workspace-app"), DefaultGrainType("apps.workspace-app")]
public interface IWorkspaceApp : INeuron
{
    Task<AppSnapshot> Install(AppManifest manifest, IReadOnlyDictionary<string, string> configuration);
    Task<AppSnapshot> Read();
    Task<AppSnapshot> Configure(IReadOnlyDictionary<string, string> configuration);
    Task<AppSnapshot> Activate();
    Task<AppSnapshot> WorkspaceActivated(long generation);
    Task<AppSnapshot> Deactivate();
    Task<AppSnapshot> Dispatch(AppDispatch request);
}
