using DigitalBrain.Core;
using DigitalBrain.Kernel;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.TestKit;

[GrainType("digitalbrain.testkit.probe.v1")]
public class ProbeNeuron(ILogger<ProbeNeuron> logger, NeuronJournals journals)
    : Neuron(logger, journals), IProbeNeuron, IHandle<ProbeMessageSynapse>
{
    public Task HandleAsync(ProbeMessageSynapse synapse, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
