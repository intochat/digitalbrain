using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.Shell;

[GrainType("shell")]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Orleans grain activated by the silo from GrainType metadata.")]
internal sealed class ShellNeuron :
    Neuron,
    IShell,
    IEmit<SceneOpened>
{
    public Task Open(OpenScene command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.SceneKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Title);

        return EmitAsync(new SceneOpened(command.CommandId, Id, command.SceneKey, command.Title));
    }
}
