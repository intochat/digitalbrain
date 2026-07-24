using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Security;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Serialization;

namespace DigitalBrain.AI;

public sealed class AIModule : IModule
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        DurablePayloadProtectionHosting.Configure(builder.Services, builder.Configuration);
        AIClients.Add(builder.Services);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void ConfigureSerialization(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSerializer(serializer => serializer.AddJsonSerializer(IsMeaiContractType));
    }

    private static bool IsMeaiContractType(Type type)
        => type == typeof(Microsoft.Extensions.AI.ChatMessage)
            || type == typeof(Microsoft.Extensions.AI.ChatResponse);
}
