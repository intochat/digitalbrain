global using ITimer = DigitalBrain.Time.Timers.ITimer;
using DigitalBrain.Time;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.Timers;
using Orleans.Configuration;

namespace DigitalBrain.Tests;

internal static class TimerTestSupport
{
    public static Task<BrainTestHost> StartAsync(CancellationToken ct, string? directory = null,
        ControlledTimers? timers = null, ReminderControl? reminders = null, bool fastCollection = false)
        => BrainTestHost.StartAsync(new()
        {
            PersistenceDirectory = directory,
            UseReminders = true,
            ConfigureSilo = silo =>
            {
                silo.AddTime();
                if (timers is not null)
                {
                    Decorate<ITimerRegistry>(silo.Services, inner => new ControlledTimerRegistry(inner, timers));
                    silo.Services.AddSingleton<TimeProvider>(timers);
                }
                if (fastCollection)
                {
                    silo.Configure<GrainCollectionOptions>(options =>
                    {
                        options.CollectionAge = TimeSpan.FromMilliseconds(200);
                        options.CollectionQuantum = TimeSpan.FromMilliseconds(100);
                    });
                }
                if (reminders is not null)
                {
                    // Accelerated real reminders are a test setting, never a production default.
                    silo.Configure<ReminderOptions>(options => options.MinimumReminderPeriod = TimeSpan.FromMilliseconds(100));
                    Decorate<IReminderRegistry>(silo.Services, inner => new FaultingReminders(inner, reminders));
                    silo.Services.AddSingleton<IIncomingGrainCallFilter>(services =>
                    {
                        reminders.Table = services.GetRequiredService<IReminderTable>();
                        return reminders;
                    });
                }
            }
        }, ct);

    private static void Decorate<T>(IServiceCollection services, Func<T, T> decorate) where T : class
    {
        var descriptor = services.Last(d => d.ServiceType == typeof(T));
        services.Remove(descriptor);
        services.AddSingleton<T>(provider => decorate((T)(descriptor.ImplementationInstance
            ?? descriptor.ImplementationFactory?.Invoke(provider)
            ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!))));
    }
}
