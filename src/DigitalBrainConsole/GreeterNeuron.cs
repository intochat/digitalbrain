using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;

namespace DigitalBrainConsole;

[Alias("DigitalBrainConsole.IGreeterNeuron")]
public interface IGreeterNeuron : INeuron;

internal sealed class GreeterNeuron(NeuronRuntime runtime) :
    Neuron(runtime),
    IGreeterNeuron,
    IHandle<UserMessageReceived>
{
    public Task HandleAsync(UserMessageReceived signal, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[greeter] handled UserMessageReceived(\"{signal.Text}\") -> \"Hello!\"");
        return Task.CompletedTask;
    }
}
