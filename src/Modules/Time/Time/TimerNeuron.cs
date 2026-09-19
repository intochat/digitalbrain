using DigitalBrain.Core;
using Orleans.Runtime;
using Orleans.Timers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace DigitalBrain.Time;

[GrainType("timer")]
internal class TimerNeuron(
    [PersistentState("state", "Default")] IPersistentState<TimerState> state,
    TimeProvider time, IReminderRegistry reminders) : Neuron, ITimer, IRemindable
{
    private bool _unavailable;
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(1);
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        if (state.State.Status == TimerStatus.Scheduled) { await RegisterAsync(state.State); }
    }
    public Task<TimerSnapshot> Read()
    {
        EnsureAvailable();
        return Task.FromResult(state.State.Snapshot());
    }
    public async Task<TimerSnapshot> Schedule(int durationSeconds, string note, long? expectedGeneration = null)
    {
        EnsureAvailable();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durationSeconds);
        ArgumentException.ThrowIfNullOrWhiteSpace(note);
        if (expectedGeneration is { } expected && expected != state.State.Generation)
        { throw new InvalidOperationException("Timer generation changed."); }
        if (state.State.Status == TimerStatus.Scheduled)
        { throw new InvalidOperationException("Timer is already scheduled."); }
        var now = time.GetUtcNow();
        var next = new TimerState
        {
            Status = TimerStatus.Scheduled,
            Generation = checked(state.State.Generation + 1),
            ScheduledAt = now,
            DueAt = now.AddSeconds(durationSeconds),
            DurationSeconds = durationSeconds,
            Note = note
        };
        await RegisterAsync(next);
        try { await SaveAsync(next); }
        catch
        {
            try { await RetireAsync(next.Generation).WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (Exception error)
            { ServiceProvider.GetRequiredService<ILogger<TimerNeuron>>().LogWarning(error, "Could not remove refused timer reminder"); }
            throw;
        }
        return next.Snapshot();
    }
    public async Task<TimerSnapshot> Stop()
    {
        EnsureAvailable();
        if (state.State.Status != TimerStatus.Scheduled) { return state.State.Snapshot(); }
        await SaveAsync(state.State with { Status = TimerStatus.Cancelled });
        await RetireAsync(state.State.Generation);
        return state.State.Snapshot();
    }
    public Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (!reminderName.StartsWith("due/", StringComparison.Ordinal) ||
            !long.TryParse(reminderName.AsSpan(4), out var generation))
        { throw new InvalidOperationException("Unknown timer reminder."); }
        return OnDueAsync(generation);
    }
    internal async Task OnDueAsync(long generation)
    {
        EnsureAvailable();
        var current = state.State;
        if (current.Status != TimerStatus.Scheduled || current.Generation != generation)
        {
            await RetireAsync(generation);
            return;
        }
        var now = time.GetUtcNow();
        if (now < current.DueAt!.Value) { return; }
        await SaveAsync(current with { Status = TimerStatus.Elapsed });
        await PublishAsync(new TimerElapsed(this.GetPrimaryKeyString(), generation, current.DueAt.Value, now,
            now - current.DueAt.Value > Period ? TimerResolution.Recovered : TimerResolution.OnTime, current.Note!));
        await RetireAsync(generation);
    }
    private async Task RegisterAsync(TimerState current)
    {
        var delay = current.DueAt!.Value - time.GetUtcNow();
        await reminders.RegisterOrUpdateReminder(this.GetGrainId(), $"due/{current.Generation}", delay > TimeSpan.Zero ? delay : TimeSpan.Zero, Period);
    }
    private async Task RetireAsync(long generation)
    {
        var reminder = await reminders.GetReminder(this.GetGrainId(), $"due/{generation}");
        if (reminder is not null) { await reminders.UnregisterReminder(this.GetGrainId(), reminder); }
    }
    private async Task SaveAsync(TimerState next)
    {
        state.State = next;
        try { await state.WriteStateAsync(); }
        catch
        {
            try { await state.ReadStateAsync(); }
            catch { _unavailable = true; DeactivateOnIdle(); }
            throw;
        }
    }
    private void EnsureAvailable()
    {
        if (_unavailable) { throw new InvalidOperationException("Timer state is unavailable; activation is stopping."); }
    }
}
