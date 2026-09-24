using DigitalBrain.Contracts;
using Orleans.Metadata;

namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("brain.workspace-activation")]
public sealed record WorkspaceActivation([property: Id(0)] long Generation = 0, [property: Id(1)] bool Active = false);

[GenerateSerializer, Alias("DigitalBrain.Activated")]
public sealed record DigitalBrainActivated([property: Id(0)] string WorkspaceId, [property: Id(1)] long Generation) : Signal;

[Alias("brain.workspace-lifecycle"), DefaultGrainType("brain.workspace-lifecycle")]
public interface IWorkspaceLifecycle : INeuron
{
    Task<WorkspaceActivation> Read();
    Task<WorkspaceActivation> Activate();
}
