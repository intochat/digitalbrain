using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Core;

[GrainType(IDigitalBrainNeuron.GrainTypeName)]
internal sealed class DigitalBrainNeuron : Neuron, IDigitalBrainNeuron
{
    private const string ActivationPublishedName = "activation-published";

    private readonly IDurableValue<bool> _activationPublished;

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

        await EmitAsync(new DigitalBrainActivated(Id.Owner)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        _activationPublished.Value = true;
        await WriteStateAsync().ConfigureAwait(true);
    }
}
