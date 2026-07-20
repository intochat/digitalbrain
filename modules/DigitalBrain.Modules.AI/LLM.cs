using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

public abstract class LLM(IChatClient chatClient) : Neuron, ILLM
{
    public Task<ChatResponse> RespondAsync(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return chatClient.GetResponseAsync(messages);
    }
}
