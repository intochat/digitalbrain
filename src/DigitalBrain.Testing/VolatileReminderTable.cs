using DigitalBrain.Abstractions;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Testing;

internal sealed class VolatileReminderTable : IReminderTable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(GrainId Grain, string Name), StoredReminder>
        _entries = [];
    private long _nextSequence;

    public Task Init() => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<ReminderEntry> ReadRow(GrainId grainId, string reminderName)
    {
        lock (_gate)
        {
            return Task.FromResult(
                _entries.TryGetValue((grainId, reminderName), out var entry)
                    ? Copy(entry.Entry)
                    : null!);
        }
    }

    public Task<ReminderTableData> ReadRows(GrainId grainId)
    {
        lock (_gate)
        {
            return Task.FromResult(new ReminderTableData(
                _entries.Values
                    .Where(entry => entry.Entry.GrainId == grainId)
                    .Select(entry => Copy(entry.Entry))));
        }
    }

    public Task<ReminderTableData> ReadRows(uint begin, uint end)
    {
        lock (_gate)
        {
            return Task.FromResult(new ReminderTableData(
                _entries.Values
                    .Where(entry => InRange(
                        entry.Entry.GrainId.GetUniformHashCode(),
                        begin,
                        end))
                    .Select(entry => Copy(entry.Entry))));
        }
    }

    public Task<string> UpsertRow(ReminderEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_gate)
        {
            var etag = Guid.NewGuid().ToString("N");
            var stored = Copy(entry);
            stored.ETag = etag;
            _entries[(stored.GrainId, stored.ReminderName)] = new(
                stored,
                Utc(stored.StartAt),
                _nextSequence++);
            return Task.FromResult(etag);
        }
    }

    public Task<bool> RemoveRow(GrainId grainId, string reminderName, string eTag)
        => Task.FromResult(
            RemoveRowWithStatus(grainId, reminderName, eTag)
                == ReminderRemovalResult.Removed);

    internal ReminderRemovalResult RemoveRowWithStatus(
        GrainId grainId,
        string reminderName,
        string eTag)
    {
        lock (_gate)
        {
            var key = (grainId, reminderName);

            if (!_entries.TryGetValue(key, out var entry))
            {
                return ReminderRemovalResult.Missing;
            }

            if (!string.Equals(
                    entry.Entry.ETag,
                    eTag,
                    StringComparison.Ordinal))
            {
                return ReminderRemovalResult.ETagMismatch;
            }

            _entries.Remove(key);
            return ReminderRemovalResult.Removed;
        }
    }

    public Task TestOnlyClearTable()
    {
        lock (_gate)
        {
            _entries.Clear();
            _nextSequence = 0;
            return Task.CompletedTask;
        }
    }

    internal DueReminder? NextDueAtOrBefore(
        DateTimeOffset target,
        string scope)
    {
        lock (_gate)
        {
            return _entries.Values
                .Where(entry =>
                    entry.NextDue <= target
                    && BelongsToScope(entry.Entry.GrainId, scope))
                .OrderBy(entry => entry.NextDue)
                .ThenBy(entry => entry.Sequence)
                .Select(entry => new DueReminder(
                    entry.Entry.GrainId,
                    entry.Entry.ReminderName,
                    entry.Entry.ETag,
                    Utc(entry.Entry.StartAt),
                    entry.NextDue,
                    entry.Entry.Period,
                    entry.Sequence))
                .FirstOrDefault();
        }
    }

    internal void CompleteDelivery(DueReminder delivered)
    {
        lock (_gate)
        {
            var key = (delivered.GrainId, delivered.ReminderName);
            if (!_entries.TryGetValue(key, out var current)
                || !string.Equals(
                    current.Entry.ETag,
                    delivered.ETag,
                    StringComparison.Ordinal)
                || current.NextDue != delivered.Due)
            {
                return;
            }

            current.NextDue = current.NextDue + current.Entry.Period;
        }
    }

    internal string DescribePendingAtOrBefore(
        DateTimeOffset target,
        string scope)
    {
        lock (_gate)
        {
            var descriptions = _entries.Values
                .Where(entry =>
                    entry.NextDue <= target
                    && BelongsToScope(entry.Entry.GrainId, scope))
                .OrderBy(entry => entry.NextDue)
                .ThenBy(entry => entry.Sequence)
                .Select(entry =>
                    $"{entry.Entry.GrainId}/{entry.Entry.ReminderName}, due={entry.NextDue:O}, period={entry.Entry.Period}, etag={entry.Entry.ETag}")
                .ToArray();

            return descriptions.Length == 0
                ? "none"
                : string.Join("; ", descriptions);
        }
    }

    private static bool InRange(uint hash, uint begin, uint end)
        => begin < end
            ? hash > begin && hash <= end
            : hash > begin || hash <= end;

    private static bool BelongsToScope(GrainId grainId, string scope)
    {
        var id = NeuronId.FromGrainKey(
            grainId.Type.ToString()
                ?? throw new InvalidOperationException(
                    "A reminder grain has no grain type."),
            grainId.Key.ToString());

        return id.Owner.Value.StartsWith(
            $"{scope}-",
            StringComparison.Ordinal);
    }

    private static DateTimeOffset Utc(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static ReminderEntry Copy(ReminderEntry entry) => new()
    {
        GrainId = entry.GrainId,
        ReminderName = entry.ReminderName,
        StartAt = entry.StartAt,
        Period = entry.Period,
        ETag = entry.ETag,
    };

    private sealed class StoredReminder(
        ReminderEntry entry,
        DateTimeOffset nextDue,
        long sequence)
    {
        internal ReminderEntry Entry { get; } = entry;

        internal DateTimeOffset NextDue { get; set; } = nextDue;

        internal long Sequence { get; } = sequence;
    }
}

internal sealed record DueReminder(
    GrainId GrainId,
    string ReminderName,
    string ETag,
    DateTimeOffset FirstTickTime,
    DateTimeOffset Due,
    TimeSpan Period,
    long Sequence);

internal enum ReminderRemovalResult
{
    Missing,
    Removed,
    ETagMismatch,
}
