using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Shell;

[GrainType("scene")]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Orleans grain activated by the silo from GrainType metadata.")]
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
