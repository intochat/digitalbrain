using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;

namespace DigitalBrainConsole;

[Alias("DigitalBrainConsole.IChatNeuron")]
public interface IChatNeuron : INeuron;

// Handles the incoming message, then broadcasts it onward as UserMessageReceived. It names no
// receiver: who hears this is a property of the graph, not of this code.
internal sealed class ChatNeuron : Neuron, IChatNeuron, IHandle<UserMessage>
{
    public async Task HandleAsync(UserMessage signal, CancellationToken cancellationToken)
    {
        var reached = await BroadcastAsync(new UserMessageReceived(signal.Text)).ConfigureAwait(true);
        Console.WriteLine($"[chat]    broadcast {signal.Text.Length} chars -> {reached} receivers");
    }
}
