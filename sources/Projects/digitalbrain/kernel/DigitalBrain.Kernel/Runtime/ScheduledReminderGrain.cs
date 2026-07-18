using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.Kernel.Runtime;

public sealed class ScheduledReminderGrain(
    [PersistentState("scheduled-reminder", "digitalbrain")] IPersistentState<ScheduledReminderState> state,
    SynapseBroadcaster broadcaster)
    : Grain, IScheduledReminderGrain, IRemindable
{
    private IDisposable? _timer;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        if (state.RecordExists && !state.State.IsFired)
        {
            var remaining = state.State.ScheduledFor - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                _timer = this.RegisterGrainTimer<object?>(
                    OnTimerTickAsync,
                    state: null,
                    new() { DueTime = remaining, Period = Timeout.InfiniteTimeSpan, Interleave = true });
            }
            else
            {
                _ = OnTimerTickAsync(null, CancellationToken.None);
            }
        }
        return base.OnActivateAsync(cancellationToken);
    }

    public async Task ScheduleReminderAsync(
        string reminderFqn,
        TimeSpan delay,
        IReadOnlyDictionary<string, string> payload)
    {
        var scheduledFor = DateTimeOffset.UtcNow + delay;
        
        state.State.ReminderFqn = reminderFqn;
        state.State.Payload = payload;
        state.State.ScheduledFor = scheduledFor;
        state.State.IsFired = false;
        await state.WriteStateAsync();

        var dueTime = delay < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : delay;
        await this.RegisterOrUpdateReminder(
            this.GetPrimaryKeyString(),
            dueTime,
            TimeSpan.FromMinutes(10));

        _timer?.Dispose();
        _timer = this.RegisterGrainTimer<object?>(
            OnTimerTickAsync,
            state: null,
            new() { DueTime = delay, Period = Timeout.InfiniteTimeSpan, Interleave = true });
    }

    private async Task OnTimerTickAsync(object? stateObj, CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        _timer = null;
        await TryFireAsync();
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        await TryFireAsync();
    }

    public Task TriggerReceiveReminderAsync(string reminderName)
    {
        return TryFireAsync();
    }

    private async Task TryFireAsync()
    {
        if (state.State.IsFired)
            return;

        state.State.IsFired = true;
        await state.WriteStateAsync();

        try
        {
            var reminder = await this.GetReminder(this.GetPrimaryKeyString());
            if (reminder != null)
            {
                await this.UnregisterReminder(reminder);
            }
        }
        catch
        {
            // Defensive swallow
        }

        if (!string.IsNullOrEmpty(state.State.ReminderFqn))
        {
            await broadcaster.BroadcastReminderAsync(
                state.State.ReminderFqn,
                state.State.Payload ?? new Dictionary<string, string>(),
                CancellationToken.None);
        }
    }
}

[GenerateSerializer]
public sealed class ScheduledReminderState
{
    [Id(0)] public string? ReminderFqn { get; set; }
    [Id(1)] public IReadOnlyDictionary<string, string>? Payload { get; set; }
    [Id(2)] public DateTimeOffset ScheduledFor { get; set; }
    [Id(3)] public bool IsFired { get; set; }
}
