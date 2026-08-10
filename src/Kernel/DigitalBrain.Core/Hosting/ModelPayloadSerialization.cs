using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace DigitalBrain.Core;

public static class ModelPayloadSerialization
{
    public static void AddModelPayloadSerialization(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSerializer(
            serializer => serializer.AddJsonSerializer(
                static type => type == typeof(Microsoft.Extensions.AI.ChatMessage)
                    || type == typeof(Microsoft.Extensions.AI.ChatResponse)
                    || type == typeof(Microsoft.Extensions.AI.ChatResponseUpdate)
                    || typeof(Microsoft.Extensions.AI.AIContent).IsAssignableFrom(type)
                    || typeof(Microsoft.Extensions.AI.AITool).IsAssignableFrom(type)));
    }
}
