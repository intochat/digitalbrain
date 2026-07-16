using Brain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using System.Reflection;

namespace Brain.Kernel;

public sealed class NeuronStateMapper : IAttributeToFactoryMapper<NeuronStateAttribute>
{
    public Factory<IGrainContext, object> GetFactory(
        ParameterInfo parameter,
        NeuronStateAttribute metadata)
    {
        if (parameter.ParameterType != typeof(NeuronDurableState))
            throw new InvalidOperationException(
                $"Parameter '{parameter.Name}' must be of type NeuronDurableState.");

        return context =>
        {
            var services = context.ActivationServices;
            return new NeuronDurableState(
                services.GetRequiredKeyedService<IDurableList<NeuronEvent>>("neuron-journal"),
                services.GetRequiredKeyedService<IDurableDictionary<string, NeuronReceipt>>("neuron-receipts"),
                services.GetRequiredKeyedService<IDurableList<SynapseRecord>>("neuron-synapses"));
        };
    }
}
