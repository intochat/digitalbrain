using Brain.Modules.Scheduling.Contracts;
using Orleans.Runtime;

namespace Brain.Modules.Scheduling;

public sealed class ScheduleGrain(
    [PersistentState("schedule", "Default")]
    IPersistentState<ScheduleState> state) : Grain, IScheduleGrain, IRemindable
{
    private const string ReminderName = "due";
    private static readonly TimeSpan ReminderPeriod = TimeSpan.FromMinutes(1);
    private readonly IPersistentState<ScheduleState> _state = state;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        if (_state.State.Status == ScheduleLifecycle.Scheduled && _state.State.DueAtUtc is { } dueAt)
        {
            await ArmReminderAsync(dueAt);
        }
    }

    public async Task<ScheduleSnapshot> ScheduleAsync(ScheduleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ArgumentException("A schedule requires title and idempotency key.");
        }

        EnsureIdentity();
        if (_state.State.ProcessedRequests.Add(request.IdempotencyKey))
        {
            if (request.DueAtUtc <= DateTimeOffset.UtcNow)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "A schedule must be due in the future.");
            }

            _state.State.Title = request.Title.Trim();
            _state.State.DueAtUtc = request.DueAtUtc.ToUniversalTime();
            _state.State.Status = ScheduleLifecycle.Scheduled;
            _state.State.TriggeredAtUtc = null;
            await _state.WriteStateAsync();
            await ArmReminderAsync(request.DueAtUtc);
        }

        return Snapshot();
    }

    public Task<ScheduleSnapshot> ReadAsync()
    {
        EnsureIdentity();
        return Task.FromResult(Snapshot());
    }

    public async Task<ScheduleSnapshot> CancelAsync(string idempotencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        EnsureIdentity();
        if (_state.State.ProcessedRequests.Add(idempotencyKey)
            && _state.State.Status == ScheduleLifecycle.Scheduled)
        {
            _state.State.Status = ScheduleLifecycle.Cancelled;
            await _state.WriteStateAsync();
            await RetireReminderAsync();
        }

        return Snapshot();
    }

    async Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
    {
        if (!string.Equals(reminderName, ReminderName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Schedule does not own reminder '{reminderName}'.");
        }

        if (_state.State.Status != ScheduleLifecycle.Scheduled || _state.State.DueAtUtc is not { } dueAt)
        {
            await RetireReminderAsync();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now < dueAt)
        {
            await ArmReminderAsync(dueAt);
            return;
        }

        _state.State.Status = ScheduleLifecycle.Triggered;
        _state.State.TriggeredAtUtc = now;
        await _state.WriteStateAsync();
        await RetireReminderAsync();
    }

    private Task<IGrainReminder> ArmReminderAsync(DateTimeOffset dueAt)
        => this.RegisterOrUpdateReminder(
            ReminderName,
            dueAt - DateTimeOffset.UtcNow is { } dueTime && dueTime > TimeSpan.Zero
                ? dueTime
                : TimeSpan.Zero,
            ReminderPeriod);

    private async Task RetireReminderAsync()
    {
        if (await this.GetReminder(ReminderName) is { } reminder)
        {
            await this.UnregisterReminder(reminder);
        }
    }

    private void EnsureIdentity()
    {
        if (_state.State.ScheduleId.Length != 0)
        {
            return;
        }

        var key = this.GetPrimaryKeyString();
        _state.State.ScheduleId = key[(key.IndexOf(':', StringComparison.Ordinal) + 1)..];
    }

    private ScheduleSnapshot Snapshot()
        => new(
            _state.State.ScheduleId,
            _state.State.Title,
            _state.State.DueAtUtc,
            _state.State.Status.ToString().ToLowerInvariant(),
            _state.State.TriggeredAtUtc);
}
