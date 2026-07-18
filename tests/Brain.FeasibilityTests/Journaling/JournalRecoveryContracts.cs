namespace Brain.FeasibilityTests.Journaling;

[Alias(nameof(IJournalRecoveryGrain))]
public interface IJournalRecoveryGrain : IGrainWithStringKey
{
    [Alias(nameof(WriteAllAsync))]
    Task WriteAllAsync(int counter, Guid mapKey, string mapValue, Guid queueItem, string listItem);

    [Alias(nameof(ReadAllAsync))]
    Task<JournalRecoverySnapshot> ReadAllAsync();

    [Alias(nameof(CommitIntentThenExternalEffectAsync))]
    Task CommitIntentThenExternalEffectAsync(int nextCounter);

    [Alias(nameof(SchedulePendingWorkAsync))]
    Task SchedulePendingWorkAsync();

    [Alias(nameof(SchedulePendingWorkAndFailAfterCommitAsync))]
    Task SchedulePendingWorkAndFailAfterCommitAsync();

    [Alias(nameof(HasPendingWorkReminderAsync))]
    Task<bool> HasPendingWorkReminderAsync();
}

[GenerateSerializer]
[Alias(nameof(JournalRecoverySnapshot))]
public sealed record JournalRecoverySnapshot(
    [property: Id(0)] int Counter,
    [property: Id(1)] Dictionary<Guid, string> Map,
    [property: Id(2)] List<Guid> Queue,
    [property: Id(3)] List<string> List,
    [property: Id(4)] bool PendingReminderWork,
    [property: Id(5)] int ReminderRecoveryCount,
    [property: Id(6)] string ReminderRecoveryInstanceId);

public sealed record JournalRecoveryClusterInstance(string Id);

public static class JournalRecoveryExternalEffectProbe
{
    private static int _count;

    public static int Count => Volatile.Read(ref _count);

    public static void Reset() => Interlocked.Exchange(ref _count, 0);

    public static void Record() => Interlocked.Increment(ref _count);
}
