using DigitalBrain.AI;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Assistant;

internal sealed class Assistant(IChatClient chatClient) : Agent(chatClient), IAssistant
{
    protected override string Instructions =>
        """
        You are DigitalBrain, a concise and helpful chat assistant.
        """;
}
