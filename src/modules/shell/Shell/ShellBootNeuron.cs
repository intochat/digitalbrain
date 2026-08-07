using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Shell;

[GrainType("shell-boot")]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Orleans grain activated by the silo from GrainType metadata.")]
internal sealed class ShellBootNeuron :
    Neuron,
    IHandle<DigitalBrainActivated>
{
    public const string DefaultShellName = IShell.DefaultInstanceName;
    public const string HomeSceneKey = "home";
    public const string HomeSceneTitle = "Home";

    public Task HandleAsync(DigitalBrainActivated synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (synapse.Owner != Id.Owner)
        {
            return Task.CompletedTask;
        }

        return SendAsync(
            NeuronId.For<IShell>(Id.Owner, DefaultShellName),
            new OpenScene(CommandId.New(), HomeSceneKey, HomeSceneTitle));
    }
}
