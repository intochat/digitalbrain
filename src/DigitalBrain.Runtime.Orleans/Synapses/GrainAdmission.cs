using DigitalBrain.Invocation;
using DigitalBrain.Kernel;
using DigitalBrain.Runtime.Orleans.Hosting;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Synapses;

internal sealed class GrainAdmission(IGrainFactory grains, NeuronTypeRegistry types) : IAdmission
{
    public async Task Admit(NeuronId subscriber, NeuronId publisher, string[] signalAliases, CancellationToken ct = default)
    {
        if (!types.TryGet(publisher.Type, out var sourceClass)) { throw new BindingException($"No neuron class is hosted for type '{publisher.Type}'."); }
        var host = grains.GetGrain<ISynapseHost>(GrainId.Create(NeuronTypeRegistry.GrainTypeOf(NeuronTypeRegistry.PolicyOf(sourceClass)), publisher.ToString()));
        var previous = RequestContext.Get(HostNeuronContext.CallerKey);
        RequestContext.Set(HostNeuronContext.CallerKey, subscriber.ToString());
        try { await host.Admit(subscriber, signalAliases).ConfigureAwait(true); }
        finally
        {
            if (previous is null) { RequestContext.Remove(HostNeuronContext.CallerKey); }
            else { RequestContext.Set(HostNeuronContext.CallerKey, previous); }
        }
    }
}
