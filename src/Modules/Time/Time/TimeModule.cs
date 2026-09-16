using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Time;

public sealed class TimeModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<TimeOptions>()
            .BindConfiguration(TimeOptions.SectionName)
            .Validate(options => options.AlarmPeriod > TimeSpan.Zero, "Alarm period must be positive.")
            .ValidateOnStart();
        builder.Services.TryAddSingleton(services => services.GetRequiredService<IOptions<TimeOptions>>().Value);
    }
}
