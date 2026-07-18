using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace Brain.FeasibilityTests.Journaling;

public sealed class JournalRecoveryGrain(
    [FromKeyedServices("counter")] IDurableValue<int> counter,
    [FromKeyedServices("map")] IDurableDictionary<Guid, string> map,
    [FromKeyedServices("queue")] IDurableQueue<Guid> queue,
    [FromKeyedServices("list")] IDurableList<string> list,
    [FromKeyedServices("pendingReminderWork")] IDurableValue<bool> pendingReminderWork,
    [FromKeyedServices("reminderRecoveryCount")] IDurableValue<int> reminderRecoveryCount,
    [FromKeyedServices("reminderRecoveryInstanceId")] IDurableValue<string> reminderRecoveryInstanceId,
    JournalRecoveryClusterInstance clusterInstance)
    : DurableGrain, IJournalRecoveryGrain, IRemindable
{
    private const string PendingWorkReminder = "pending-work";
    private readonly IDurableValue<int> _counter = counter;
    private readonly IDurableDictionary<Guid, string> _map = map;
    private readonly IDurableQueue<Guid> _queue = queue;
    private readonly IDurableList<string> _list = list;
    private readonly IDurableValue<bool> _pendingReminderWork = pendingReminderWork;
    private readonly IDurableValue<int> _reminderRecoveryCount = reminderRecoveryCount;
    private readonly IDurableValue<string> _reminderRecoveryInstanceId = reminderRecoveryInstanceId;
    private readonly JournalRecoveryClusterInstance _clusterInstance = clusterInstance;

    public async Task WriteAllAsync(int counter, Guid mapKey, string mapValue, Guid queueItem, string listItem)
    {
        _counter.Value = counter;
        _map[mapKey] = mapValue;
        _queue.Enqueue(queueItem);
        _list.Add(listItem);
        await WriteStateAsync();
    }

    public Task<JournalRecoverySnapshot> ReadAllAsync()
    {
        return Task.FromResult(new JournalRecoverySnapshot(
            _counter.Value,
            new Dictionary<Guid, string>(_map),
            _queue.ToList(),
            _list.ToList(),
            _pendingReminderWork.Value,
            _reminderRecoveryCount.Value,
            _reminderRecoveryInstanceId.Value ?? ""));
    }

    public async Task CommitIntentThenExternalEffectAsync(int nextCounter)
    {
        _counter.Value = nextCounter;
        await WriteStateAsync();
        JournalRecoveryExternalEffectProbe.Record();
    }

    public Task SchedulePendingWorkAsync() =>
        SchedulePendingWorkCoreAsync(false);

    public Task SchedulePendingWorkAndFailAfterCommitAsync() =>
        SchedulePendingWorkCoreAsync(true);

    public async Task<bool> HasPendingWorkReminderAsync()
    {
        return await this.GetReminder(PendingWorkReminder) is not null;
    }

    private async Task SchedulePendingWorkCoreAsync(bool failAfterCommit)
    {
        await this.RegisterOrUpdateReminder(
            PendingWorkReminder,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMinutes(1));
        _pendingReminderWork.Value = true;
        await WriteStateAsync();

        if (failAfterCommit)
            throw new InvalidOperationException("Injected failure after durable commit.");
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (!string.Equals(
                reminderName,
                PendingWorkReminder,
                StringComparison.Ordinal))
        {
            return;
        }

        if (_pendingReminderWork.Value)
        {
            _pendingReminderWork.Value = false;
            _reminderRecoveryCount.Value++;
            _reminderRecoveryInstanceId.Value = _clusterInstance.Id;
            await WriteStateAsync();
        }

        var reminder = await this.GetReminder(PendingWorkReminder);
        if (reminder is not null)
            await this.UnregisterReminder(reminder);
    }
}
