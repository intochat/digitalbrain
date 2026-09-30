using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Compute.Storage;

// A non-reentrant writer serializes deduplication and root publication. Children
// commit first; storage failure cannot expose a partial batch or double charge.
[GrainType("compute.records")]
internal sealed class ComputeRecordsNeuron(
    [PersistentState("records", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ComputeRecordsState> state)
    : Neuron, IComputeRecords
{
    private ComputeRecordTree Tree => new(
        id => GrainFactory.GetGrain<IComputePart>(id).Read(),
        (id, json) => GrainFactory.GetGrain<IComputePart>(id).Put(json));

    public async Task<int> Put(ComputeStoredRecord[] records, bool overwrite)
    {
        if (records.Length > 1000) { throw new ArgumentException("At most 1000 records per batch."); }
        var root = state.State.Root;
        var tree = Tree;
        var changed = 0;
        foreach (var record in records)
        {
            var old = await tree.Get(root, record.Id);
            if (old is not null && (!overwrite || record.Revision < old.Revision)) { continue; }
            var next = old is null ? record : record with { SortKey = old.SortKey };
            if (old == next) { continue; }
            root = await tree.Set(root, next);
            changed++;
        }
        if (changed > 0) { await Save(new() { Root = root }); }
        return changed;
    }

    public async Task<ComputeStoredRecord[]> Read()
    {
        var result = new List<ComputeStoredRecord>();
        await foreach (var record in Tree.Read(state.State.Root)) { result.Add(record); }
        return [.. result.OrderBy(record => record.SortKey, StringComparer.Ordinal)];
    }

    public async Task<ComputeRecordPage> Page(string? before, int limit)
    {
        if (limit is < 1 or > 100) { throw new ArgumentException("Invalid page size."); }
        // Compatibility APIs expose timestamp cursors. Scan immutable leaves but
        // retain only a bounded page; no activation owns an unbounded history list.
        var selected = new SortedDictionary<string, ComputeStoredRecord>(StringComparer.Ordinal);
        await foreach (var record in Tree.Read(state.State.Root))
        {
            if (before is not null && StringComparer.Ordinal.Compare(record.SortKey, before) >= 0) { continue; }
            selected[record.SortKey] = record;
            if (selected.Count > limit + 1) { selected.Remove(selected.First().Key); }
        }
        var rows = selected.Reverse().Take(limit).Select(entry => entry.Value).ToArray();
        return new(rows, selected.Count > limit ? rows[^1].SortKey : null);
    }

    private async Task Save(ComputeRecordsState next)
    {
        var previous = state.State;
        state.State = next;
        try { await state.WriteStateAsync(); }
        catch { state.State = previous; throw; }
    }
}
