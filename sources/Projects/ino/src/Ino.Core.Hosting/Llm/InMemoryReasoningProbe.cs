using System.Collections.Concurrent;

namespace Ino.Core.Hosting.Llm;

/// <summary>
/// Default <see cref="IReasoningProbe"/>. Thread-safe; last-write-wins per
/// neuron. In-memory only — a later slice can back this with synapse journal
/// for cross-silo / persistent reasoning history.
/// </summary>
public sealed class InMemoryReasoningProbe : IReasoningProbe
{
    readonly ConcurrentDictionary<string, ReasoningRecord> _last = new(StringComparer.Ordinal);

    public void Record(string neuronId, ReasoningRecord record) =>
        _last[neuronId] = record;

    public bool TryGet(string neuronId, out ReasoningRecord record)
    {
        if (_last.TryGetValue(neuronId, out var hit))
        {
            record = hit;
            return true;
        }
        record = default!;
        return false;
    }

    public IReadOnlyList<string> KnownNeurons() => _last.Keys.ToArray();
}
