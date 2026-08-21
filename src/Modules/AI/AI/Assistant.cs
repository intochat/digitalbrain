using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Assistant;
internal sealed class Assistant([FromKeyedServices(typeof(IGemma4))] IChatClient chatClient)
    : Agent(chatClient), IAssistant
{
    protected override string? Instructions =>
        $$"""
        You are DigitalBrain, a concise and helpful chat assistant.
        """;
}
