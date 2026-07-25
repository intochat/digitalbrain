using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.AI;

public abstract class LLM(IChatClient chatClient) : Neuron, ILLM
{
    public Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return chatClient.GetResponseAsync(messages);
    }
}

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class LlmAttribute<TModel> : FromKeyedServicesAttribute
    where TModel : LLM
{
    public LlmAttribute() : base(typeof(TModel))
    {
    }
}
