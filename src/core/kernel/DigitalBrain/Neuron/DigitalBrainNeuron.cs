using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Kernel;

[GrainType(IDigitalBrainNeuron.GrainTypeName)]
internal sealed class DigitalBrainNeuron : Neuron, IDigitalBrainNeuron
{
    private const string ActivationPublishedName = "activation-published";

    private readonly IDurableValue<bool> _activationPublished;

    public DigitalBrainNeuron()
    {
        _activationPublished = ServiceProvider.GetRequiredKeyedService<IDurableValue<bool>>(
            ActivationPublishedName);
    }

    public async Task Activate()
    {
        if (_activationPublished.Value)
        {
            return;
        }

        await EmitAsync(new DigitalBrainActivated(Id.Owner));
        _activationPublished.Value = true;
        await WriteStateAsync();
    }
}
