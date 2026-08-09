using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Shell;

[GrainType("shell")]
internal sealed class ShellNeuron :
    Neuron,
    IShell,
    IHandle<OpenScene>,
    IEmit<SceneOpened>
{
    public Task HandleAsync(OpenScene synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(synapse.SceneKey)
            || string.IsNullOrWhiteSpace(synapse.Title))
        {
            return Task.CompletedTask;
        }

        return EmitAsync(new SceneOpened(synapse.CommandId, Id, synapse.SceneKey, synapse.Title));
    }
}
