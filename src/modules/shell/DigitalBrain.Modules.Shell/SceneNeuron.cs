using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.Shell;

[GrainType("scene")]
internal sealed class SceneNeuron :
    Neuron,
    IScene,
    IHandle<ControlActivated>
{
    public Task HandleAsync(ControlActivated synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        ArgumentException.ThrowIfNullOrWhiteSpace(synapse.SceneKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(synapse.ControlId);
        ArgumentException.ThrowIfNullOrWhiteSpace(synapse.Intent);
        return Task.CompletedTask;
    }
}
