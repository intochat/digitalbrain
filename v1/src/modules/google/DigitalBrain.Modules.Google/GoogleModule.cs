using DigitalBrain.Abstractions;
using DigitalBrain.Security;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace DigitalBrain.Google;

public sealed partial class GoogleModule : IModule
{
    static partial void ConfigureRuntime(ISiloBuilder builder)
    {
        DurablePayloadProtectionHosting.Configure(builder.Services, builder.Configuration);
    }

    static partial void ConfigureSerialization(IServiceCollection services)
        => services.AddSerializer(
            serializer => serializer.AddJsonSerializer(
                static type => type == typeof(Microsoft.Extensions.AI.ChatMessage)
                    || type == typeof(Microsoft.Extensions.AI.ChatResponse)
                    || type == typeof(Microsoft.Extensions.AI.ChatResponseUpdate)
                    || typeof(Microsoft.Extensions.AI.AIContent).IsAssignableFrom(type)
                    || typeof(Microsoft.Extensions.AI.AITool).IsAssignableFrom(type)));
}
