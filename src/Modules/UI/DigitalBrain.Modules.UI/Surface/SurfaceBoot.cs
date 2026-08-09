using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.UI;

[GrainType("surface-boot")]
internal sealed class SurfaceBoot :
    Neuron,
    IHandle<DigitalBrainActivated>
{
    public const string DefaultSurfaceName = ISurface.DefaultInstanceName;
    public const string HomeSurfaceKey = "home";
    public const string HomeSurfaceTitle = "Home";

    public Task HandleAsync(DigitalBrainActivated synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (synapse.Owner != Id.Owner)
        {
            return Task.CompletedTask;
        }

        return SendAsync(
            NeuronId.For<ISurface>(Id.Owner, DefaultSurfaceName),
            new OpenSurface(CommandId.New(), HomeSurfaceKey, HomeSurfaceTitle));
    }
}
