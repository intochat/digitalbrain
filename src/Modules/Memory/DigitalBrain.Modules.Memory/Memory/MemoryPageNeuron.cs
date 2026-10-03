using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace DigitalBrain.Memory;

[GrainType("memory.page")]
internal sealed class MemoryPageNeuron([PersistentState("entries", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<MemoryPageState> store) : Neuron, IMemoryPage
{
    internal const int Capacity = 64;
    public async Task<bool> Put(VectorMemoryEntry entry)
    {
        Validate(entry);
        if (!store.State.Entries.ContainsKey(entry.Key) && !store.State.Deleted.ContainsKey(entry.Key)
            && store.State.Entries.Count + store.State.Deleted.Count >= Capacity)
        { throw new InvalidOperationException("This memory partition is full. Choose another namespace."); }
        var deleted = new Dictionary<string, MemoryDeletion>(store.State.Deleted);
        deleted.Remove(entry.Key);
        await Save(store.State with
        {
            Entries = new(store.State.Entries) { [entry.Key] = entry },
            Deleted = deleted,
            Pending = new(store.State.Pending) { entry.Key },
        });
        return true;
    }
    public async Task<bool> Remove(string name, string @namespace, string key)
    {
        var entries = new Dictionary<string, VectorMemoryEntry>(store.State.Entries);
        var removed = entries.Remove(key);
        if (!removed && !store.State.Deleted.ContainsKey(key) && store.State.Deleted.Count + entries.Count >= Capacity)
        { throw new InvalidOperationException("This memory partition is full."); }
        await Save(store.State with { Entries = entries, Deleted = new(store.State.Deleted) { [key] = new(name, @namespace) }, Pending = new(store.State.Pending) { key } });
        return removed;
    }
    public Task<int> Count() => Task.FromResult(store.State.Entries.Count);
    public Task<bool> Contains(string key) => Task.FromResult(store.State.Entries.ContainsKey(key) || store.State.Deleted.ContainsKey(key));
    public Task<bool> HasCapacity() => Task.FromResult(store.State.Entries.Count + store.State.Deleted.Count < Capacity);
    public Task Clear() => Save(new());
    public Task<MemoryMatch[]> Search(float[] vector, int limit, Dictionary<string, string> tags) => Task.FromResult(store.State.Entries.Values
        .Where(entry => tags.All(tag => entry.Tags.Any(value => value.Name == tag.Key && value.Value == tag.Value)))
        .Select(entry => new MemoryMatch(new(entry.Key, entry.Text, entry.Tags, entry.Payload), Cosine(vector, entry.Embedding)))
        .OrderByDescending(match => match.Score).ThenBy(match => match.Note.Key, StringComparer.Ordinal).Take(limit).ToArray());

    public async Task<MemoryIndexResult> Rebuild()
    {
        var index = ServiceProvider.GetService<IVectorMemoryStore>();
        if (index is null) { return new(false, 0, store.State.Entries.Count + store.State.Deleted.Count); }
        var pending = new HashSet<string>(store.State.Entries.Keys.Concat(store.State.Deleted.Keys), StringComparer.Ordinal);
        var indexed = 0;
        foreach (var key in pending.ToArray())
        {
            try
            {
                if (store.State.Entries.TryGetValue(key, out var entry)) { await index.UpsertAsync(entry, CancellationToken.None); }
                else { var deleted = store.State.Deleted[key]; await index.RemoveAsync(deleted.Name, deleted.Namespace, key, CancellationToken.None); }
                pending.Remove(key);
                indexed++;
            }
            catch (Exception) { /* Canonical state remains intact; the durable queue is retried explicitly. */ }
        }
        await Save(store.State with { Pending = pending });
        return new(true, indexed, pending.Count);
    }
    internal static void Validate(VectorMemoryEntry entry)
    {
        if (entry.Text.Length is 0 or > 16384 || entry.Key.Length is 0 or > 512 || entry.Namespace.Length is 0 or > 512
            || entry.Embedding.Length is 0 or > 8192 || entry.Embedding.Any(value => !float.IsFinite(value))
            || entry.Tags.Length > 64 || entry.Tags.Any(tag => tag.Name.Length > 256 || tag.Value.Length > 1024)
            || entry.Tags.Select(tag => tag.Name).Distinct(StringComparer.Ordinal).Count() != entry.Tags.Length)
        { throw new ArgumentException("Memory exceeds the bounded entry limits or contains invalid vectors/tags."); }
    }
    private static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length) { throw new InvalidOperationException("The embedding dimensions changed; use a matching embedding model."); }
        double dot = 0, aa = 0, bb = 0;
        for (var i = 0; i < a.Length; i++) { dot += (double)a[i] * b[i]; aa += (double)a[i] * a[i]; bb += (double)b[i] * b[i]; }
        return aa == 0 || bb == 0 ? 0 : dot / Math.Sqrt(aa * bb);
    }
    private async Task Save(MemoryPageState next)
    {
        var old = store.State;
        store.State = next;
        try { await store.WriteStateAsync(); }
        catch { store.State = old; throw; }
    }
}
