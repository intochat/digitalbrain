using System.Text;
using DigitalBrain.Coding;

namespace DigitalBrain.Behavior;

internal sealed class BehaviorLogStore(string root, long maximumBytes)
{
    private readonly DurableDocumentStore<LogDocument> _store = new(root, () => new());
    public Task<long> AppendAsync(string id, Guid generation, string stream, string message, CancellationToken ct)
        => _store.UpdateAsync(id, d =>
        {
            d.Id = id;
            if (message.Length > 16384) { message = message[..16384]; }
            d.Entries.Add(new(++d.Sequence, generation, DateTimeOffset.UtcNow, stream, message));
            var bytes = d.Entries.Sum(x => Encoding.UTF8.GetByteCount(x.Message));
            while (d.Entries.Count > 0 && (bytes > maximumBytes || d.Entries.Count > 10000))
            { bytes -= Encoding.UTF8.GetByteCount(d.Entries[0].Message); d.Entries.RemoveAt(0); }
            return d.Sequence;
        }, ct);

    public Task<BehaviorLogPage> ReadAsync(string id, long after, int limit, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(after);
        if (limit is < 1 or > 500) { throw new ArgumentOutOfRangeException(nameof(limit)); }
        return _store.ReadAsync(id, d => new BehaviorLogPage(d.Entries.Where(e => e.Sequence > after).Take(limit).ToArray(),
            d.Sequence, d.Entries.Count == 0 ? d.Sequence > after : after < d.Entries[0].Sequence - 1), ct);
    }
}

internal sealed class LogDocument
{
    public string Id { get; set; } = "";
    public long Sequence { get; set; }
    public List<BehaviorLogEntry> Entries { get; set; } = [];
}
