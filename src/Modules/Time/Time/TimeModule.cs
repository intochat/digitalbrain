using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Time;

public sealed class TimeModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<TimeOptions>();
    }
}
