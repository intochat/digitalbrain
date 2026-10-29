using System.Text.Json;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>Durable reminders in one JSON file, <c>&lt;Home&gt;/state/reminders.json</c>, rewritten atomically under a lock.</summary>
public sealed class FileReminderTable : IReminderTable, IDisposable
{
    /// <summary>The file name under <c>&lt;Home&gt;/state</c>.</summary>
    public const string FileName = "reminders.json";

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates a table over <paramref name="path"/>.</summary>
    public FileReminderTable(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
    }

    /// <summary>Creates the table for a Home.</summary>
    public FileReminderTable(StateHome home) : this(System.IO.Path.Combine((home ?? throw new ArgumentNullException(nameof(home))).State, FileName)) { }

    /// <summary>The file.</summary>
    public string Path { get; }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<ReminderTableData> ReadRows(GrainId grainId)
    {
        var key = grainId.ToString();
        return Locked(rows => new ReminderTableData(rows.Where(r => r.GrainId == key).Select(ToEntry).ToList()));
    }

    /// <inheritdoc />
    public Task<ReminderTableData> ReadRows(uint begin, uint end)
        => Locked(rows => new ReminderTableData(rows.Where(r => InRange(r.Hash, begin, end)).Select(ToEntry).ToList()));

    /// <inheritdoc />
    public Task<ReminderEntry?> ReadRow(GrainId grainId, string reminderName)
    {
        var key = grainId.ToString();
        return Locked(rows => rows.Where(r => r.GrainId == key && r.Name == reminderName).Select(ToEntry).FirstOrDefault());
    }

    /// <inheritdoc />
    public async Task<string?> UpsertRow(ReminderEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var rows = await Load().ConfigureAwait(false);
            var key = entry.GrainId.ToString();
            rows.RemoveAll(r => r.GrainId == key && r.Name == entry.ReminderName);
            var etag = Guid.NewGuid().ToString("N");
            var start = entry.StartAt.ToUniversalTime();
            start = new DateTime(start.Ticks - (start.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
            rows.Add(new Row(key, entry.ReminderName, start, entry.Period, etag, entry.GrainId.GetUniformHashCode()));
            await Save(rows).ConfigureAwait(false);
            return etag;
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveRow(GrainId grainId, string reminderName, string eTag)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var rows = await Load().ConfigureAwait(false);
            var key = grainId.ToString();
            var removed = rows.RemoveAll(r => r.GrainId == key && r.Name == reminderName && r.ETag == eTag);
            if (removed == 0) { return false; }
            await Save(rows).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task TestOnlyClearTable()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await Save([]).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    /// <summary>The ring range <c>(begin, end]</c>, wrapping when begin is not below end; <c>(x, x]</c> is everything.</summary>
    internal static bool InRange(uint hash, uint begin, uint end)
        => begin < end ? hash > begin && hash <= end : hash > begin || hash <= end;

    private async Task<T> Locked<T>(Func<List<Row>, T> read)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return read(await Load().ConfigureAwait(false)); }
        finally { _gate.Release(); }
    }

    private static ReminderEntry ToEntry(Row row) => new()
    {
        GrainId = GrainId.Parse(row.GrainId, System.Globalization.CultureInfo.InvariantCulture),
        ReminderName = row.Name,
        StartAt = row.StartAt,
        Period = row.Period,
        ETag = row.ETag,
    };

    private async Task<List<Row>> Load()
    {
        if (!File.Exists(Path)) { return []; }
        var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using (stream.ConfigureAwait(false))
        {
            return [.. await JsonSerializer.DeserializeAsync(stream, FileStorageJson.Default.RowArray).ConfigureAwait(false) ?? []];
        }
    }

    private Task Save(List<Row> rows)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var snapshot = rows.ToArray();
        return AtomicFile.Write(Path, stream => JsonSerializer.SerializeAsync(stream, snapshot, FileStorageJson.Default.RowArray));
    }

    internal sealed record Row(string GrainId, string Name, DateTime StartAt, TimeSpan Period, string ETag, uint Hash);
}
