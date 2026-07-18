using DigitalBrain;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using System.Reflection;

namespace DigitalBrain.Kernel;

public sealed class NeuronStateMapper : IAttributeToFactoryMapper<NeuronStateAttribute>
{
    public Factory<IGrainContext, object> GetFactory(
        ParameterInfo parameter,
        NeuronStateAttribute metadata)
    {
        if (parameter.ParameterType != typeof(NeuronDurableState))
            throw new InvalidOperationException(
                $"Parameter '{parameter.Name}' must be of type {nameof(NeuronDurableState)}.");

        return context =>
        {
            var services = context.ActivationServices;
            return new NeuronDurableState(
                services.GetRequiredKeyedService<IDurableValue<NeuronStatus>>(nameof(NeuronDurableState.Status)),
                services.GetRequiredKeyedService<IDurableDictionary<Guid, ExternalOperation>>(nameof(NeuronDurableState.Operations)),
                services.GetRequiredKeyedService<IDurableDictionary<Guid, NeuronNotification>>(nameof(NeuronDurableState.Outbox)));
        };
    }
}
