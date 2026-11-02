using System.Collections.Concurrent;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>In-memory reminder table scoped to a host path, surviving in-process restarts without disk I/O.</summary>
internal sealed class MemoryReminderTable : IReminderTable
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ReminderEntry>> Stores = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, ReminderEntry> _entries;

    public static MemoryReminderTable For(string scope)
        => new(Stores.GetOrAdd(scope, _ => new(StringComparer.Ordinal)));

    public MemoryReminderTable(ConcurrentDictionary<string, ReminderEntry> entries) => _entries = entries;

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<ReminderTableData> ReadRows(GrainId grainId)
    {
        var rows = _entries.Values.Where(r => r.GrainId == grainId).ToList();
        return Task.FromResult(new ReminderTableData(rows));
    }

    public Task<ReminderTableData> ReadRows(uint begin, uint end)
    {
        var rows = _entries.Values.Where(r => InRange((uint)r.GrainId.GetUniformHashCode(), begin, end)).ToList();
        return Task.FromResult(new ReminderTableData(rows));
    }

    public Task<ReminderEntry?> ReadRow(GrainId grainId, string reminderName)
    {
        _entries.TryGetValue(Key(grainId, reminderName), out var entry);
        return Task.FromResult<ReminderEntry?>(entry);
    }

    public Task<string?> UpsertRow(ReminderEntry entry)
    {
        var nextEtag = Guid.NewGuid().ToString("N");
        var copy = new ReminderEntry
        {
            GrainId = entry.GrainId,
            ReminderName = entry.ReminderName,
            StartAt = entry.StartAt,
            Period = entry.Period,
            ETag = nextEtag,
        };
        _entries[Key(entry.GrainId, entry.ReminderName)] = copy;
        return Task.FromResult<string?>(nextEtag);
    }

    public Task<bool> RemoveRow(GrainId grainId, string reminderName, string eTag)
    {
        var key = Key(grainId, reminderName);
        if (_entries.TryGetValue(key, out var existing))
        {
            if (existing.ETag == eTag)
            {
                return Task.FromResult(_entries.TryRemove(key, out _));
            }
            return Task.FromResult(false);
        }
        return Task.FromResult(false);
    }

    public Task TestOnlyClearTable()
    {
        _entries.Clear();
        return Task.CompletedTask;
    }

    private static string Key(GrainId grainId, string name) => $"{grainId}|{name}";

    internal static bool InRange(uint hash, uint begin, uint end)
    {
        if (begin < end) { return hash > begin && hash <= end; }
        return hash > begin || hash <= end;
    }
}
