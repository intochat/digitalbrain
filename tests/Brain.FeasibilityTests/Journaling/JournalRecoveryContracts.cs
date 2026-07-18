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
}

[GenerateSerializer]
[Alias(nameof(JournalRecoverySnapshot))]
public sealed record JournalRecoverySnapshot(
    [property: Id(0)] int Counter,
    [property: Id(1)] Dictionary<Guid, string> Map,
    [property: Id(2)] List<Guid> Queue,
    [property: Id(3)] List<string> List);

public static class JournalRecoveryExternalEffectProbe
{
    private static int _count;

    public static int Count => Volatile.Read(ref _count);

    public static void Reset() => Interlocked.Exchange(ref _count, 0);

    public static void Record() => Interlocked.Increment(ref _count);
}
