global using ITimer = DigitalBrain.Time.Timers.ITimer;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.Timers;

namespace DigitalBrain.Tests;

internal static class TimeTestControls
{
    public static void UseControlledTimers(this ISiloBuilder silo, ControlledTimers timers)
    {
        Decorate<ITimerRegistry>(silo.Services, inner => new ControlledTimerRegistry(inner, timers));
        silo.Services.AddSingleton<TimeProvider>(timers);
    }

    public static void UseFastCollection(this ISiloBuilder silo)
        => silo.Configure<GrainCollectionOptions>(options =>
        {
            options.CollectionAge = TimeSpan.FromMilliseconds(200);
            options.CollectionQuantum = TimeSpan.FromMilliseconds(100);
        });

    public static void UseReminderControl(this ISiloBuilder silo, ReminderControl reminders)
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

    private static void Decorate<T>(IServiceCollection services, Func<T, T> decorate) where T : class
    {
        var descriptor = services.Last(d => d.ServiceType == typeof(T));
        services.Remove(descriptor);
        services.AddSingleton<T>(provider => decorate((T)(descriptor.ImplementationInstance
            ?? descriptor.ImplementationFactory?.Invoke(provider)
            ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!))));
    }
}
