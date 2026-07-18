using DigitalBrain.V2.Core.Runtime;
using Ping.Contracts;

namespace Ping;

// Software 1.0 implementation. The interface (IPingNeuron) declares the wiring; this supplies
// the body. Handling a Ping broadcasts a Pong — the C# twin of Ping.ino's `on ping: emit pong`.
public sealed class PingNeuron : Neuron, IPingNeuron
{
    public async Task HandleAsync(Contracts.Ping synapse, CancellationToken ct)
    {
        State["lastSeen"] = synapse.From;
        await Emit(new Pong(To: synapse.From));
    }
}
