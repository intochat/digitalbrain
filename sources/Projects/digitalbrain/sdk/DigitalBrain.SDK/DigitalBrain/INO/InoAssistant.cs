using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.SDK.DigitalBrain.Ai;

namespace DigitalBrain.SDK.DigitalBrain.INO;

[GrainType("DigitalBrain.InoAssistant")]
[Neuron]
internal sealed partial class InoAssistant : Neuron,
      ICallNeuronTarget,
      IHandle<InoChatRequest>,
      IHandle<LlmResponse>
{
    public Task HandleAsync(InoChatRequest synapse, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task HandleAsync(LlmResponse synapse, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<string> AskAsync(string prompt) => Task.FromResult(string.Empty);
}
