using DigitalBrain.V2.Core.Runtime;
using Greeter.Contracts;

namespace Greeter;

public sealed class GreeterNeuron : Neuron, IGreeterNeuron
{
    public Task HandleAsync(Hello synapse, CancellationToken ct) =>
        Ask<IRoomNeuron>("default", new Announce(synapse.Name));
}
