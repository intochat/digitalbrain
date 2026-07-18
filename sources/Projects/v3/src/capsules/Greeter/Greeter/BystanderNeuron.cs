using DigitalBrain.V2.Core.Runtime;
using Greeter.Contracts;

namespace Greeter;

public sealed class BystanderNeuron : Neuron, IBystanderNeuron
{
    public Task HandleAsync(Hello synapse, CancellationToken ct) =>
        Emit(new BystanderHeardHello(synapse.Name));
}
