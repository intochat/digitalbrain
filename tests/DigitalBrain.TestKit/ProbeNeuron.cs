using DigitalBrain.Core;
using DigitalBrain.Kernel;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DigitalBrain.TestKit;

[GrainType("digitalbrain.testkit.probe.v1")]
public class ProbeNeuron(ILogger<ProbeNeuron> logger, NeuronJournals journals)
    : Neuron(logger, journals), IProbeNeuron, IHandle<ProbeMessageSynapse>
{
    public Task HandleAsync(ProbeMessageSynapse synapse, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task FireJsonSignalAsync(string signalName, string json, CancellationToken cancellationToken = default)
    {
        var props = JsonSerializer.Deserialize<Dictionary<string, object?>>(json)
            ?? throw new ArgumentException("JSON signal payload must be an object.", nameof(json));

        return FireAsync(new Signal(signalName, props), cancellationToken);
    }
}
