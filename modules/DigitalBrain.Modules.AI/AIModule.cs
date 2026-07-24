using DigitalBrain.Abstractions;
using DigitalBrain.Security;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Serialization;

namespace DigitalBrain.AI;

public sealed partial class AIModule : IModule
{
    static partial void ConfigureSerialization(IServiceCollection services)
        => services.AddSerializer(
            serializer => serializer.AddJsonSerializer(IsMeaiContractType));

    static partial void ConfigureRuntime(ISiloBuilder builder)
    {
        DurablePayloadProtectionHosting.Configure(builder.Services, builder.Configuration);
        AIClients.Add(builder.Services);
    }

    private static bool IsMeaiContractType(Type type)
        => type == typeof(Microsoft.Extensions.AI.ChatMessage)
            || type == typeof(Microsoft.Extensions.AI.ChatResponse);
}
