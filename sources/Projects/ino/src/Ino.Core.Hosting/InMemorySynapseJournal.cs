using System.Collections.Concurrent;

namespace Ino.Core.Hosting;

/// <summary>
/// Bounded ring-buffer journal. The most recent <see cref="Capacity"/> fires and
/// broadcasts are retained; older entries drop silently. Per-target counters
/// accumulate across the lifetime of the process so metrics survive ring churn.
/// Thread-safe; the ring is protected by a lock, counters are lock-free.
/// </summary>
public sealed class InMemorySynapseJournal : ISynapseJournal
{
    public const int Capacity = 500;

    readonly SynapseJournalEntry[] _ring = new SynapseJournalEntry[Capacity];
    int _head;
    int _count;
    readonly object _ringLock = new();

    readonly ConcurrentDictionary<string, NeuronCounters> _counters = new();

    public void Record(SynapseJournalEntry entry)
    {
        lock (_ringLock)
        {
            _ring[_head] = entry;
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        var counters = _counters.GetOrAdd(entry.TargetNeuron, _ => new NeuronCounters());
        counters.Record(entry);
    }

    public IReadOnlyList<SynapseJournalEntry> Recent(string? neuronId, int limit)
    {
        if (limit <= 0) return Array.Empty<SynapseJournalEntry>();

        SynapseJournalEntry[] snapshot;
        int count;
        int head;
        lock (_ringLock)
        {
            count = _count;
            head = _head;
            snapshot = new SynapseJournalEntry[count];
            for (var i = 0; i < count; i++)
            {
                var idx = (head - count + i + Capacity) % Capacity;
                snapshot[i] = _ring[idx];
            }
        }

        IEnumerable<SynapseJournalEntry> source = snapshot;
        if (!string.IsNullOrWhiteSpace(neuronId))
        {
            source = source.Where(e =>
                e.TargetNeuron == neuronId || e.SourceNeuron == neuronId);
        }

        return source
            .Reverse()
            .Take(limit)
            .ToList();
    }

    public NeuronMetricsSnapshot Metrics(string? neuronId)
    {
        IEnumerable<KeyValuePair<string, NeuronCounters>> source = _counters;
        if (!string.IsNullOrWhiteSpace(neuronId))
            source = source.Where(kv => kv.Key == neuronId);

        var list = source
            .Select(kv => new NeuronMetric(
                NeuronId: kv.Key,
                FireCount: Interlocked.Read(ref kv.Value.FireCount),
                BroadcastCount: Interlocked.Read(ref kv.Value.BroadcastCount),
                LastActivatedUnixMs: Interlocked.Read(ref kv.Value.LastActivatedUnixMs)))
            .OrderByDescending(m => m.FireCount + m.BroadcastCount)
            .ToList();

        return new NeuronMetricsSnapshot(list);
    }

    sealed class NeuronCounters
    {
        public long FireCount;
        public long BroadcastCount;
        public long LastActivatedUnixMs;

        public void Record(SynapseJournalEntry entry)
        {
            if (entry.Kind == "SynapseBroadcast")
                Interlocked.Increment(ref BroadcastCount);
            else
                Interlocked.Increment(ref FireCount);
            Interlocked.Exchange(ref LastActivatedUnixMs, entry.TimestampUnixMs);
        }
    }
}
