using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace DigitalBrain.Chat;

public sealed partial class ChatModule : IModule
{
    static partial void ConfigureSerialization(IServiceCollection services)
        => services.AddSerializer(
            serializer => serializer.AddJsonSerializer(
                static type => type == typeof(Microsoft.Extensions.AI.ChatMessage)
                    || type == typeof(Microsoft.Extensions.AI.ChatResponse)
                    || type == typeof(Microsoft.Extensions.AI.ChatResponseUpdate)
                    || typeof(Microsoft.Extensions.AI.AIContent).IsAssignableFrom(type)));
}
