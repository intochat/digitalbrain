global using ITimer = DigitalBrain.Time.ITimer;
using DigitalBrain.Time;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;
using Orleans.Timers;
namespace DigitalBrain.Tests;

internal static class TimerTestSupport
{
    public static Task<BrainTestHost> StartAsync(CancellationToken ct, string? directory = null, StorageFaults? faults = null, TimeProvider? time = null, ReminderFaults? reminders = null)
        => BrainTestHost.StartAsync(new()
        {
            PersistenceDirectory = directory, UseReminders = true, StorageFaults = faults,
            ConfigureSilo = silo =>
            {
                silo.AddTime();
                if (time is not null) { silo.Services.AddSingleton(time); }
                if (reminders is not null)
                {
                    var descriptor = silo.Services.Last(d => d.ServiceType == typeof(IReminderRegistry));
                    silo.Services.Remove(descriptor);
                    silo.Services.AddSingleton<IReminderRegistry>(services => new FaultingReminders(
                        (IReminderRegistry)(descriptor.ImplementationInstance ?? descriptor.ImplementationFactory?.Invoke(services)
                            ?? ActivatorUtilities.CreateInstance(services, descriptor.ImplementationType!)), reminders));
                }
            }
        }, ct);
}

public interface ITimerTestDriver : ITimer { Task InvokeDue(long generation); }
[GrainType("timer-test-driver")]
internal sealed class TimerTestDriverGrain(
    [PersistentState("state", "Default")] IPersistentState<TimerState> state,
    TimeProvider time, IReminderRegistry reminders) : TimerNeuron(state, time, reminders), ITimerTestDriver
{
    public Task InvokeDue(long generation) => OnDueAsync(generation);
}
internal sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan duration) => _now += duration;
}
internal sealed class ReminderFaults
{
    public bool FailRegister { get; set; }
    public bool FailUnregister { get; set; }
}
internal sealed class FaultingReminders(IReminderRegistry inner, ReminderFaults faults) : IReminderRegistry
{
    public Task<IGrainReminder> RegisterOrUpdateReminder(GrainId id, string name, TimeSpan due, TimeSpan period)
    {
        if (faults.FailRegister) { faults.FailRegister = false; throw new IOException("Refused reminder registration."); }
        return inner.RegisterOrUpdateReminder(id, name, due, period);
    }
    public Task UnregisterReminder(GrainId id, IGrainReminder reminder)
    {
        if (faults.FailUnregister) { faults.FailUnregister = false; throw new IOException("Refused reminder removal."); }
        return inner.UnregisterReminder(id, reminder);
    }
    public Task<IGrainReminder?> GetReminder(GrainId id, string name) => inner.GetReminder(id, name);
    public Task<List<IGrainReminder>> GetReminders(GrainId id) => inner.GetReminders(id);
}
