using System.Text.Json;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Testing;

internal sealed class FileReminderTable(string root) : IReminderTable
{
    private readonly Lock _gate = new();
    private readonly string _path = Path.Combine(root, "reminders.json");

    public Task Init() => Task.CompletedTask;

    public Task<ReminderTableData> ReadRows(GrainId grainId)
    {
        lock (_gate)
        {
            return Task.FromResult(new ReminderTableData([.. Read().Where(row => row.GrainId == grainId)]));
        }
    }

    public Task<ReminderTableData> ReadRows(uint begin, uint end)
    {
        lock (_gate)
        {
            return Task.FromResult(new ReminderTableData([.. Read().Where(row =>
            {
                var hash = row.GrainId.GetUniformHashCode();
                return begin < end ? begin < hash && hash <= end : hash > begin || hash <= end;
            })]));
        }
    }

    public Task<ReminderEntry?> ReadRow(GrainId grainId, string reminderName)
    {
        lock (_gate)
        {
            return Task.FromResult(Read().Find(row => row.GrainId == grainId && row.ReminderName == reminderName));
        }
    }

    public Task<string?> UpsertRow(ReminderEntry entry)
    {
        lock (_gate)
        {
            var rows = Read();
            rows.RemoveAll(row => row.GrainId == entry.GrainId && row.ReminderName == entry.ReminderName);
            entry.ETag = Guid.NewGuid().ToString("N");
            rows.Add(entry);
            Write(rows);
            return Task.FromResult<string?>(entry.ETag);
        }
    }

    public Task<bool> RemoveRow(GrainId grainId, string reminderName, string eTag)
    {
        lock (_gate)
        {
            var rows = Read();
            var removed = rows.RemoveAll(row => row.GrainId == grainId && row.ReminderName == reminderName && row.ETag == eTag) > 0;
            if (removed)
            {
                Write(rows);
            }
            return Task.FromResult(removed);
        }
    }

    public Task TestOnlyClearTable()
    {
        lock (_gate) { File.Delete(_path); }
        return Task.CompletedTask;
    }

    private List<ReminderEntry> Read() => File.Exists(_path)
        ? [.. JsonSerializer.Deserialize<List<Row>>(File.ReadAllBytes(_path))!.Select(row => new ReminderEntry
        {
            GrainId = GrainId.Parse(row.GrainId), ReminderName = row.ReminderName,
            StartAt = row.StartAt, Period = row.Period, ETag = row.ETag,
        })] : [];

    private void Write(List<ReminderEntry> rows)
    {
        Directory.CreateDirectory(root);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, rows.Select(row => new Row(row.GrainId.ToString(), row.ReminderName, row.StartAt, row.Period, row.ETag)));
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    private sealed record Row(string GrainId, string ReminderName, DateTime StartAt, TimeSpan Period, string? ETag);
}
