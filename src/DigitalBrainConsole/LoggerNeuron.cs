using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;

namespace DigitalBrainConsole;

[Alias("DigitalBrainConsole.ILoggerNeuron")]
public interface ILoggerNeuron : INeuron;

internal sealed class LoggerNeuron(NeuronRuntime runtime) :
    Neuron(runtime),
    ILoggerNeuron,
    IHandle<UserMessageReceived>
{
    public Task HandleAsync(UserMessageReceived signal, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[logger]  recorded \"{signal.Text}\"");
        return Task.CompletedTask;
    }
}
