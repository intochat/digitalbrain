using Orleans.Concurrency;

namespace Brain.Abstractions.Journal;

[GenerateSerializer, Immutable]
public sealed record BrainJournalPage
{
    public BrainJournalPage(
        string workspaceId,
        Guid activityId,
        long afterSequence,
        long lastSequence,
        IReadOnlyList<BrainJournalRecord> records,
        bool hasMore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        if (activityId == Guid.Empty)
        {
            throw new ArgumentException("An activity identity is required.", nameof(activityId));
        }
        if (afterSequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(afterSequence));
        }
        if (lastSequence < afterSequence)
        {
            throw new ArgumentOutOfRangeException(nameof(lastSequence));
        }
        ArgumentNullException.ThrowIfNull(records);

        long previous = afterSequence;
        foreach (var record in records)
        {
            if (!string.Equals(workspaceId, record.WorkspaceId, StringComparison.Ordinal)
                || activityId != record.ActivityId)
            {
                throw new ArgumentException("Every journal record must belong to the page workspace and activity.", nameof(records));
            }
            if (record.Sequence <= previous)
            {
                throw new ArgumentException("Journal records must be strictly monotonic after the requested sequence.", nameof(records));
            }
            previous = record.Sequence;
        }
        if (records.Count > 0 && lastSequence != previous)
        {
            throw new ArgumentException("The page last sequence must match its final record.", nameof(lastSequence));
        }

        WorkspaceId = workspaceId;
        ActivityId = activityId;
        AfterSequence = afterSequence;
        LastSequence = lastSequence;
        Records = records.ToArray();
        HasMore = hasMore;
    }

    [Id(0)] public string WorkspaceId { get; }
    [Id(1)] public Guid ActivityId { get; }
    [Id(2)] public long AfterSequence { get; }
    [Id(3)] public long LastSequence { get; }
    [Id(4)] public IReadOnlyList<BrainJournalRecord> Records { get; }
    [Id(5)] public bool HasMore { get; }
}
