using DigitalBrain.Kernel;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Spi;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Hosting;

internal sealed class HostConsumer(NeuronHostGrainBase grain) : ISignalConsumer
{
    public bool TakesUntyped => grain.ActiveNeuron is ISynapseReceiver or ISignalSink;

    public async Task Consume(JournaledSignal signal, Func<IDisposable> openScope, CancellationToken ct)
    {
        var neuron = grain.ActiveNeuron ?? throw new InvalidOperationException($"Neuron {signal.Publisher} has no active instance.");
        var previous = RequestContext.Get(HostNeuronContext.CallerKey);
        RequestContext.Remove(HostNeuronContext.CallerKey);
        try
        {
            using var causeScope = CurrentCause.Open(signal);
            using var scope = openScope();
            await Reactions.Dispatch(neuron, signal, ct).ConfigureAwait(true);
        }
        finally
        {
            if (previous is not null)
            {
                RequestContext.Set(HostNeuronContext.CallerKey, previous);
            }
        }
    }
}
