using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("brain.workspace-lifecycle-state")]
public sealed class WorkspaceLifecycleState
{
    [Id(0)] public WorkspaceActivation Activation { get; set; } = new();
}

[GrainType("brain.workspace-lifecycle")]
internal sealed class WorkspaceLifecycleNeuron(
    [PersistentState("lifecycle", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<WorkspaceLifecycleState> state)
    : Neuron<WorkspaceLifecycleState>(state), IWorkspaceLifecycle
{
    public Task<WorkspaceActivation> Read() => Task.FromResult(Snapshot.Activation);
    public async Task<WorkspaceActivation> Activate()
    {
        if (Snapshot.Activation.Active) { return Snapshot.Activation; }
        var activation = new WorkspaceActivation(Snapshot.Activation.Generation + 1, true);
        await Save(new() { Activation = activation }, new DigitalBrainActivated(this.GetPrimaryKeyString(), activation.Generation));
        return activation;
    }
}
