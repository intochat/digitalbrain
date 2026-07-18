using DigitalBrain.V2.Core.Runtime;
using Greeter.Contracts;

namespace Greeter;

public sealed class RoomNeuron : Neuron, IRoomNeuron
{
    public Task HandleAsync(Announce synapse, CancellationToken ct) =>
        Emit(new Announced(synapse.Name));
}
