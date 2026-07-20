using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Testing;

internal sealed class VolatileReminderTable : IReminderTable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(GrainId Grain, string Name), ReminderEntry> _entries = [];

    public Task Init() => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<ReminderEntry> ReadRow(GrainId grainId, string reminderName)
    {
        lock (_gate)
        {
            return Task.FromResult(
                _entries.TryGetValue((grainId, reminderName), out var entry)
                    ? Copy(entry)
                    : null!);
        }
    }

    public Task<ReminderTableData> ReadRows(GrainId grainId)
    {
        lock (_gate)
        {
            return Task.FromResult(new ReminderTableData(
                _entries.Values
                    .Where(entry => entry.GrainId == grainId)
                    .Select(Copy)));
        }
    }

    public Task<ReminderTableData> ReadRows(uint begin, uint end)
    {
        lock (_gate)
        {
            return Task.FromResult(new ReminderTableData(
                _entries.Values
                    .Where(entry => InRange(entry.GrainId.GetUniformHashCode(), begin, end))
                    .Select(Copy)));
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
            _entries[(stored.GrainId, stored.ReminderName)] = stored;
            return Task.FromResult(etag);
        }
    }

    public Task<bool> RemoveRow(GrainId grainId, string reminderName, string eTag)
    {
        lock (_gate)
        {
            var key = (grainId, reminderName);

            if (!_entries.TryGetValue(key, out var entry)
                || !string.Equals(entry.ETag, eTag, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            _entries.Remove(key);
            return Task.FromResult(true);
        }
    }

    public Task TestOnlyClearTable()
    {
        lock (_gate)
        {
            _entries.Clear();
            return Task.CompletedTask;
        }
    }

    private static bool InRange(uint hash, uint begin, uint end)
        => begin < end
            ? hash > begin && hash <= end
            : hash > begin || hash <= end;

    private static ReminderEntry Copy(ReminderEntry entry) => new()
    {
        GrainId = entry.GrainId,
        ReminderName = entry.ReminderName,
        StartAt = entry.StartAt,
        Period = entry.Period,
        ETag = entry.ETag,
    };
}
