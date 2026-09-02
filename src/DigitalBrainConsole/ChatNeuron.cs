using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;

namespace DigitalBrainConsole;

[Alias("DigitalBrainConsole.IChatNeuron")]
public interface IChatNeuron : INeuron;

// Handles the message, then broadcasts it onward. It names no receiver: who hears this is a
// property of the graph, not of this code. ChatNeuron itself declares IHandle<UserMessageReceived>
// and broadcasts that same signal — SignalRouter.Resolve excludes the emitter from its own
// receiver set, so this does not route back to chat:main.
internal sealed class ChatNeuron(NeuronRuntime runtime) :
    Neuron(runtime),
    IChatNeuron,
    IHandle<UserMessageReceived>
{
    public async Task HandleAsync(UserMessageReceived signal, CancellationToken cancellationToken)
    {
        var reached = await BroadcastAsync(signal).ConfigureAwait(true);
        Console.WriteLine($"[chat]    broadcast {signal.Text.Length} chars -> {reached} receivers");
    }
}
