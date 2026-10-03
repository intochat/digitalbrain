using System.Text.Json;
using DigitalBrain.Compute.Usage;

namespace DigitalBrain.Compute.Storage;

[GenerateSerializer]
internal sealed record ComputeStoredRecord(
    [property: Id(0)] string Id,
    [property: Id(1)] string Payload,
    [property: Id(2)] string SortKey,
    [property: Id(3)] long Revision);

// Copy-on-write radix tree. Every immutable leaf has at most 64 rows, every
// branch at most 16 references. Only publishing the new root changes the view.
internal sealed class ComputeRecordTree(Func<string, Task<string>> read, Func<string, string, Task> write)
{
    // One serialized part per operation avoids reading a just-committed leaf back
    // over the network. Deserialize on every use so updates cannot mutate old roots.
    private (string Key, string Text)? _lastPart;

    private sealed class Node
    {
        public SortedDictionary<string, ComputeStoredRecord>? Records { get; init; }
        public SortedDictionary<string, string>? Children { get; init; }
    }

    private async Task<Node> Load(string root)
    {
        var text = _lastPart is { } cached && cached.Key == root ? cached.Text : await read(root);
        if (UsagePaging.Hash(text) != root) { throw new InvalidDataException("Compute record checksum mismatch."); }
        _lastPart = (root, text);
        return JsonSerializer.Deserialize<Node>(text) ?? throw new InvalidDataException("Missing compute node.");
    }

    public async Task<ComputeStoredRecord?> Get(string? root, string id)
    {
        var hash = UsagePaging.Hash(id);
        var depth = 0;
        while (root is not null)
        {
            var node = await Load(root);
            if (node.Records is { } records)
            {
                var found = records.GetValueOrDefault(hash);
                if (found is not null && found.Id != id) { throw new InvalidDataException("Compute record identity collision."); }
                return found;
            }
            root = node.Children!.GetValueOrDefault(hash[depth++].ToString());
        }
        return null;
    }

    public Task<string> Set(string? root, ComputeStoredRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Id);
        if (record.Id.Length > 4096 || record.Payload.Length > 1024 * 1024 || record.SortKey.Length > 256)
        { throw new ArgumentException("Compute record exceeds its size limit."); }
        return Update(root, UsagePaging.Hash(record.Id), record, 0);
    }

    private async Task<string> Update(string? root, string hash, ComputeStoredRecord record, int depth)
    {
        var node = root is null ? new Node { Records = [] } : await Load(root);
        if (node.Records is { } records)
        {
            if (records.TryGetValue(hash, out var existing) && existing.Id != record.Id)
            { throw new InvalidDataException("Compute record identity collision."); }
            records[hash] = record;
            return await Build(records, depth);
        }
        var children = node.Children!;
        var prefix = hash[depth].ToString();
        children[prefix] = await Update(children.GetValueOrDefault(prefix), hash, record, depth + 1);
        return await Save(node);
    }

    private async Task<string> Build(SortedDictionary<string, ComputeStoredRecord> records, int depth)
    {
        var leaf = new Node { Records = records };
        if (records.Count <= 64 && (records.Count <= 1 || JsonSerializer.Serialize(leaf).Length <= 128 * 1024))
        { return await Save(leaf); }
        if (depth >= 64) { throw new InvalidDataException("Compute tree identity collision."); }
        var children = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in records.GroupBy(entry => entry.Key[depth].ToString()))
        {
            children[group.Key] = await Build(new(group.ToDictionary(entry => entry.Key, entry => entry.Value), StringComparer.Ordinal), depth + 1);
        }
        return await Save(new Node { Children = children });
    }

    private async Task<string> Save(Node node)
    {
        var text = JsonSerializer.Serialize(node);
        var key = UsagePaging.Hash(text);
        await write(key, text);
        _lastPart = (key, text);
        return key;
    }

    public async IAsyncEnumerable<ComputeStoredRecord> Read(string? root)
    {
        if (root is null) { yield break; }
        var node = await Load(root);
        if (node.Records is { } records)
        {
            foreach (var record in records.Values) { yield return record; }
        }
        else
        {
            foreach (var child in node.Children!.Values)
            { await foreach (var record in Read(child)) { yield return record; } }
        }
    }
}
