namespace DigitalBrain.Testing.Mechanics;

public sealed class WorkspaceMarkerProbe(IWorkspaceMarker marker) : Neuron, INeuron<MechanicsStart>
{
    public Task HandleAsync(MechanicsStart synapse, CancellationToken cancellationToken)
    {
        Emit(new WorkspaceMarkerObserved(marker.Label));
        return Task.CompletedTask;
    }
}
