using DigitalBrain.Core;

namespace DigitalBrain.Kernel.Gateway;

[GrainType("digitalbrain.ingress")]
public sealed class IngressNeuron(ILogger<IngressNeuron> logger, NeuronJournals journals)
    : Neuron(logger, journals), IIngressNeuron
{
    public Task IngestAsync(string signalName, IReadOnlyDictionary<string, object?> props, CancellationToken cancellationToken = default) =>
        Broadcast(new Signal(signalName, props), cancellationToken);
}
