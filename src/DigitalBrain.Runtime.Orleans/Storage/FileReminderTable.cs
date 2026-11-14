using System.Globalization;
using System.Text.Json;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>File-backed reminder table storing Orleans reminders atomically under <c>&lt;Home&gt;/state/reminders.json</c>.</summary>
internal sealed class FileReminderTable : IReminderTable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private readonly string _filePath;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ReminderEntry> _entries = new(StringComparer.Ordinal);
    private bool _loaded;

    public FileReminderTable(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            LoadLocked();
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<ReminderTableData> ReadRows(GrainId grainId)
    {
        lock (_gate)
        {
            LoadLocked();
            var rows = _entries.Values.Where(r => r.GrainId == grainId).Select(Clone).ToList();
            return Task.FromResult(new ReminderTableData(rows));
        }
    }

    public Task<ReminderTableData> ReadRows(uint begin, uint end)
    {
        lock (_gate)
        {
            LoadLocked();
            var rows = _entries.Values.Where(r => MemoryReminderTable.InRange((uint)r.GrainId.GetUniformHashCode(), begin, end)).Select(Clone).ToList();
            return Task.FromResult(new ReminderTableData(rows));
        }
    }

    public Task<ReminderEntry?> ReadRow(GrainId grainId, string reminderName)
    {
        lock (_gate)
        {
            LoadLocked();
            return Task.FromResult(_entries.TryGetValue(Key(grainId, reminderName), out var entry) ? Clone(entry) : null);
        }
    }

    public Task<string?> UpsertRow(ReminderEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            LoadLocked();
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
            FlushLocked();
            return Task.FromResult<string?>(nextEtag);
        }
    }

    public Task<bool> RemoveRow(GrainId grainId, string reminderName, string eTag)
    {
        lock (_gate)
        {
            LoadLocked();
            var key = Key(grainId, reminderName);
            if (!_entries.TryGetValue(key, out var existing) || !string.Equals(existing.ETag, eTag, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }
            if (!_entries.Remove(key))
            {
                return Task.FromResult(false);
            }
            FlushLocked();
            return Task.FromResult(true);
        }
    }

    public Task TestOnlyClearTable()
    {
        lock (_gate)
        {
            _entries.Clear();
            _loaded = true;
            FlushLocked();
            return Task.CompletedTask;
        }
    }

    private void LoadLocked()
    {
        if (_loaded) { return; }
        _entries.Clear();
        if (File.Exists(_filePath))
        {
            var bytes = File.ReadAllBytes(_filePath);
            if (bytes.Length > 0)
            {
                var rows = JsonSerializer.Deserialize<StoredReminderRow[]>(bytes, JsonOptions) ?? [];
                foreach (var row in rows)
                {
                    if (string.IsNullOrWhiteSpace(row.GrainId) || string.IsNullOrWhiteSpace(row.ReminderName)) { continue; }
                    var grainId = GrainId.Parse(row.GrainId, CultureInfo.InvariantCulture);
                    var entry = new ReminderEntry
                    {
                        GrainId = grainId,
                        ReminderName = row.ReminderName,
                        StartAt = DateTime.SpecifyKind(row.StartAtUtc, DateTimeKind.Utc),
                        Period = TimeSpan.FromTicks(row.PeriodTicks),
                        ETag = row.ETag,
                    };
                    _entries[Key(grainId, row.ReminderName)] = entry;
                }
            }
        }
        _loaded = true;
    }

    private void FlushLocked()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        var rows = _entries.Values
            .OrderBy(r => r.GrainId.ToString(), StringComparer.Ordinal)
            .ThenBy(r => r.ReminderName, StringComparer.Ordinal)
            .Select(r => new StoredReminderRow(
                r.GrainId.ToString(),
                r.ReminderName,
                r.StartAt.Kind == DateTimeKind.Utc ? r.StartAt : r.StartAt.ToUniversalTime(),
                r.Period.Ticks,
                r.ETag ?? string.Empty))
            .ToArray();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(rows, JsonOptions);
        var tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tempPath)) { File.Delete(tempPath); } } catch { }
            throw;
        }
    }

    private static ReminderEntry Clone(ReminderEntry entry) => new()
    {
        GrainId = entry.GrainId,
        ReminderName = entry.ReminderName,
        StartAt = entry.StartAt,
        Period = entry.Period,
        ETag = entry.ETag,
    };

    private static string Key(GrainId grainId, string name) => $"{grainId}|{name}";

    private sealed record StoredReminderRow(
        string GrainId,
        string ReminderName,
        DateTime StartAtUtc,
        long PeriodTicks,
        string ETag);
}
