using DigitalBrain.Core;
using DigitalBrain.Core.Registry;
using DigitalBrain.Time.Reminders;
using DigitalBrain.Time.Timers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Hosting;

namespace DigitalBrain.Time;

[ModuleConfiguration(typeof(TimeConfigurationContract))]
public sealed class TimeModule : IModule
{
    public static ModuleDefinition Define() => new(typeof(TimeModule));
    public void Configure(ISiloBuilder silo)
    {
        silo.AddTime();
        // Test and local development may accelerate reminders; production keeps the Orleans default.
        silo.Services.AddOptions<ReminderOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            if (TimeSpan.TryParse(configuration["DigitalBrain:Time:MinimumReminderPeriod"], out var period) && period > TimeSpan.Zero)
            { options.MinimumReminderPeriod = period; }
        });
    }
}
