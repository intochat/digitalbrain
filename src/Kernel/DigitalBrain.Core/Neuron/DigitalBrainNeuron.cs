using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Core;

[GrainType(IDigitalBrainNeuron.GrainTypeName)]
internal sealed class DigitalBrainNeuron : Neuron, IDigitalBrainNeuron
{
    private const string ActivationPublishedName = "activation-published";

    private readonly IDurableValue<bool> _activationPublished;

    protected override bool RegistersWithBrain => false;

    public DigitalBrainNeuron()
    {
        _activationPublished = ServiceProvider.GetRequiredKeyedService<IDurableValue<bool>>(ActivationPublishedName);
    }

    public async Task Activate()
    {
        if (_activationPublished.Value)
        {
            return;
        }

        // Directed, not Emit: SurfaceBoot used to receive DigitalBrainActivated only as a
        // per-correlation broadcast ghost. Broadcast is now opt-in and empty by default;
        // the stable surface-boot instance is the real boot path.
        await SendAsync(
                new NeuronId(SurfaceBootGrainType, Id.Owner, SurfaceBootInstanceName),
                new DigitalBrainActivated(Id.Owner))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        _activationPublished.Value = true;
        await WriteStateAsync().ConfigureAwait(true);
    }

    // Grain type string matches SurfaceBoot's [GrainType]; name is the stable instance.
    private const string SurfaceBootGrainType = "surface-boot";
    private const string SurfaceBootInstanceName = "default";
}
