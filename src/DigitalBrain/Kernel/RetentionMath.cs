namespace DigitalBrain.Kernel;

/// <summary>
/// Pure calculation of journal retention floor and compaction boundaries based on subscriber cursor positions
/// and brain retention window policy. Zero dependencies on Orleans or test frameworks.
/// </summary>
internal static class RetentionMath
{
    /// <summary>
    /// Calculates the retention floor sequence:
    /// <c>max(0, min(lowestLive ?? long.MaxValue, last - max(windowSignals, newerThanAge)))</c>.
    /// </summary>
    /// <param name="last">The sequence number of the most recent journal entry.</param>
    /// <param name="windowSignals">Minimum number of signals to retain in the history window.</param>
    /// <param name="newerThanAge">Number of signals that fall within the time-based history window.</param>
    /// <param name="lowestLive">The minimum sequence position across all live subscribers, or null if none.</param>
    /// <returns>The highest sequence number that may be safely trimmed/compacted.</returns>
    public static long Floor(long last, int windowSignals, long newerThanAge, long? lowestLive)
    {
        var window = Math.Max(windowSignals, newerThanAge);
        return Math.Max(0, Math.Min(lowestLive ?? long.MaxValue, last - window));
    }

    /// <summary>Finds the lowest cursor sequence among active subscribers, or null if empty.</summary>
    public static long? LowestCursor(IEnumerable<long> subscriberCursors)
    {
        ArgumentNullException.ThrowIfNull(subscriberCursors);
        long? lowest = null;
        foreach (var cursor in subscriberCursors)
        {
            if (lowest is null || cursor < lowest.Value)
            {
                lowest = cursor;
            }
        }

        return lowest;
    }

    /// <summary>Computes the retention floor using an enumerable of subscriber cursor sequences.</summary>
    public static long ComputeFloor(long last, int windowSignals, long newerThanAge, IEnumerable<long> subscriberCursors)
    {
        var lowest = LowestCursor(subscriberCursors);
        return Floor(last, windowSignals, newerThanAge, lowest);
    }

    /// <summary>Whether a new retention floor allows journal compaction relative to the current floor.</summary>
    public static bool ShouldCompact(long newFloor, long currentFloor) => newFloor > currentFloor;

    /// <summary>Whether a journal entry with sequence <paramref name="entrySequence"/> is retained after compaction to <paramref name="retentionFloor"/>.</summary>
    public static bool IsRetained(long entrySequence, long retentionFloor) => entrySequence > retentionFloor;

    /// <summary>Gets the range of sequence numbers to compact from <paramref name="currentFloor"/> to <paramref name="newFloor"/>.</summary>
    public static (long FromSequence, long ToSequence)? CompactionRange(long currentFloor, long newFloor)
    {
        if (newFloor <= currentFloor) { return null; }
        return (currentFloor + 1, newFloor);
    }
}
