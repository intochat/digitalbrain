using DigitalBrain.Core;
using DigitalBrain.Time.Reminders.Signals;
using Orleans.Runtime;
using Orleans.Timers;
namespace DigitalBrain.Time.Reminders;

[GrainType("reminder")]
internal sealed class ReminderNeuron(TimeProvider time, IReminderRegistry reminders) : Neuron, IReminder, IRemindable
{
    private const string Name = "tick";
    private const string JobName = "job";

    public async Task Start(TimeSpan dueTime, TimeSpan period)
    {
        // Orleans validates due time and the configured minimum period before writing the registration.
        await reminders.RegisterOrUpdateReminder(this.GetGrainId(), Name, dueTime, period);
    }

    public async Task Stop()
    {
        var reminder = await reminders.GetReminder(this.GetGrainId(), Name);
        if (reminder is not null) { await reminders.UnregisterReminder(this.GetGrainId(), reminder); }
    }

    public async Task StartJob(TimeSpan dueTime, TimeSpan period)
        => await reminders.RegisterOrUpdateReminder(this.GetGrainId(), JobName, dueTime, period);

    public async Task StopJob()
    {
        var reminder = await reminders.GetReminder(this.GetGrainId(), JobName);
        if (reminder is not null) { await reminders.UnregisterReminder(this.GetGrainId(), reminder); }
    }

    public Task ReceiveReminder(string reminderName, TickStatus status)
    {
        var observedAt = time.GetUtcNow();
        return reminderName switch
        {
            Name => PublishAsync(new ReminderTick(this.GetPrimaryKeyString(), observedAt)),
            JobName => GrainFactory.GetGrain<IReminderJob>(this.GetPrimaryKeyString()).RunJob(observedAt),
            _ => Task.CompletedTask,
        };
    }
}