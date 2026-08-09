using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace DigitalBrain.Aspire;

public static class DigitalBrainClientWireSerializers
{
    public static IServiceCollection AddDigitalBrainClientWireSerializers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddSerializer(
            serializer => serializer.AddJsonSerializer(IsStreamingWireType));
    }

    internal static bool IsStreamingWireType(Type type)
        => type == typeof(ChatMessage)
            || type == typeof(ChatResponse)
            || type == typeof(ChatResponseUpdate)
            || typeof(AIContent).IsAssignableFrom(type);
}
