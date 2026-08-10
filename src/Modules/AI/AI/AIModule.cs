using DigitalBrain.Security;

namespace DigitalBrain.AI;

public sealed class AIModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        DurablePayloadProtectionHosting.Configure(builder.Services, builder.Configuration);
        AIClients.Add(builder.Services);
    }
}
