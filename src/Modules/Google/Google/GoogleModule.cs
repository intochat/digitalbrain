using DigitalBrain.Security;

namespace DigitalBrain.Google;

public sealed class GoogleModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        DurablePayloadProtectionHosting.Configure(builder.Services, builder.Configuration);
    }
}
