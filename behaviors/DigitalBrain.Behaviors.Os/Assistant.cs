using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Os;

internal sealed class Assistant(
    [FromKeyedServices(typeof(Llama32))] IChatClient chatClient)
    : Agent(chatClient), IAssistant
{
    protected override IReadOnlyList<CapabilityTool> Tools => [];
}
