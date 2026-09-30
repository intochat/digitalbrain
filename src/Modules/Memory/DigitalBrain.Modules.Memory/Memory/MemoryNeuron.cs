using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Memory.Signals;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Memory;

// Canonical text, metadata and vectors live in bounded Default-storage neurons.
// Qdrant is an optional, explicitly rebuildable projection.
[GrainType("memory")]
internal sealed class MemoryNeuron(TimeProvider time,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<MemoryState> state) : Neuron, IMemory
{
    private MemoryState Current => state.State is { Namespaces: not null } current ? current
        : (state.State ?? new(0, 0, DateTimeOffset.UnixEpoch)) with { Namespaces = [] };
    private string Owner => this.GetPrimaryKeyString();
    public async Task<MemoryKey> Remember(Remember note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var key = RequireKey(note.Namespace, note.Key);
        if (string.IsNullOrWhiteSpace(note.Text)) { throw new ArgumentException("Provide non-blank text to remember.", nameof(note)); }
        var generated = await Embeddings().GenerateAsync([note.Text]);
        var entry = new VectorMemoryEntry(Owner, key.Namespace, key.Key, note.Text, note.Tags, note.Payload, generated[0].Vector.ToArray());
        MemoryPageNeuron.Validate(entry);
        await (await EnsurePage(key.Namespace, key.Key)).Put(entry);
        await Save(Current with { RememberedCount = Current.RememberedCount + 1, LastChangedAt = time.GetUtcNow() });
        await PublishAsync(new Remembered(key));
        return key;
    }
    public async Task<MemoryKey> Forget(Forget note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var key = RequireKey(note.Namespace, note.Key);
        await (await EnsurePage(key.Namespace, key.Key)).Remove(Owner, key.Namespace, key.Key);
        await Save(Current with { ForgottenCount = Current.ForgottenCount + 1, LastChangedAt = time.GetUtcNow() });
        await PublishAsync(new Forgotten(key));
        return key;
    }
    public async Task<long> PurgeNamespace(PurgeNamespace note)
    {
        ArgumentNullException.ThrowIfNull(note);
        RequireKey(note.Namespace, "purge");
        var previous = Current.Namespaces.GetValueOrDefault(note.Namespace) ?? new();
        long count = 0;
        foreach (var id in previous.Pages) { count += await Page(note.Namespace, previous.Generation, id).Count(); }
        var next = new MemoryNamespace
        {
            Generation = checked(previous.Generation + 1), IndexPurgePending = true,
            RetiredPages = new(previous.RetiredPages) { [previous.Generation] = previous.Pages },
        };
        await Save(Current with { Namespaces = new(Current.Namespaces) { [note.Namespace] = next }, ForgottenCount = Current.ForgottenCount + (int)Math.Min(count, int.MaxValue), LastChangedAt = time.GetUtcNow() });
        await ClearRetired(note.Namespace);
        if (ServiceProvider.GetService<IVectorMemoryStore>() is { } index)
        {
            try { await index.RemoveNamespaceAsync(Owner, note.Namespace, CancellationToken.None); }
            catch (Exception error) { throw new InvalidOperationException("Canonical memory was purged, but the optional index purge is pending. Retry purge or rebuild the index.", error); }
            await Save(Current with { Namespaces = new(Current.Namespaces) { [note.Namespace] = Current.Namespaces[note.Namespace] with { IndexPurgePending = false } } });
        }
        await PublishAsync(new NamespacePurged(note.Namespace, count));
        return count;
    }
    [ReadOnly]
    public async Task<RecallResult> Recall(Recall query)
    {
        ArgumentNullException.ThrowIfNull(query);
        RequireKey(query.Namespace, "query");
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Query);
        if (query.Limit is < 1 or > 32) { throw new ArgumentOutOfRangeException(nameof(query.Limit)); }
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in query.Tags)
        { if (!tags.TryAdd(tag.Name, tag.Value)) { throw new ArgumentException("Duplicate filter tag.", nameof(query)); } }
        if (!Current.Namespaces.TryGetValue(query.Namespace, out var metadata)) { return new([]); }
        var generated = await Embeddings().GenerateAsync([query.Query]);
        var vector = generated[0].Vector.ToArray();
        var matches = new List<MemoryMatch>();
        foreach (var id in metadata.Pages)
        {
            matches.AddRange(await Page(query.Namespace, metadata.Generation, id).Search(vector, query.Limit, tags));
            matches = matches.OrderByDescending(match => match.Score).ThenBy(match => match.Note.Key, StringComparer.Ordinal).Take(query.Limit).ToList();
        }
        return new(matches.Select(match => match.Note).ToArray());
    }
    public async Task<MemoryIndexResult> RebuildIndex(string @namespace)
    {
        RequireKey(@namespace, "index");
        await ClearRetired(@namespace);
        var index = ServiceProvider.GetService<IVectorMemoryStore>();
        var metadata = Current.Namespaces.GetValueOrDefault(@namespace) ?? new();
        if (index is null) { return new(false, 0, metadata.Pages.Count); }
        if (metadata.IndexPurgePending)
        {
            try { await index.RemoveNamespaceAsync(Owner, @namespace, CancellationToken.None); }
            catch (Exception) { return new(true, 0, metadata.Pages.Count + 1); }
            metadata = metadata with { IndexPurgePending = false };
            await Save(Current with { Namespaces = new(Current.Namespaces) { [@namespace] = metadata } });
        }
        var indexed = 0;
        var pending = 0;
        foreach (var id in metadata.Pages)
        {
            var result = await Page(@namespace, metadata.Generation, id).Rebuild();
            indexed += result.Indexed; pending += result.Pending;
        }
        return new(true, indexed, pending);
    }
    private IEmbeddingGenerator<string, Embedding<float>> Embeddings() => ServiceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>()
        ?? throw new InvalidOperationException("Memory requires an embedding generator.");
    private async Task<IMemoryPage> EnsurePage(string @namespace, string key)
    {
        var bucket = SHA256.HashData(Encoding.UTF8.GetBytes(key))[0];
        var metadata = Current.Namespaces.GetValueOrDefault(@namespace) ?? new();
        var chain = metadata.Pages.Where(page => page % 256 == bucket).Order().ToArray();
        foreach (var id in chain)
        {
            var existing = Page(@namespace, metadata.Generation, id);
            if (await existing.Contains(key)) { return existing; }
        }
        foreach (var id in chain)
        {
            var existing = Page(@namespace, metadata.Generation, id);
            if (await existing.HasCapacity()) { return existing; }
        }
        if (Current.Namespaces.Count >= 1024 && !Current.Namespaces.ContainsKey(@namespace)) { throw new InvalidOperationException("Memory has reached its namespace limit."); }
        var page = chain.Length == 0 ? bucket : checked(chain[^1] + 256);
        metadata = metadata with { Pages = new(metadata.Pages) { page } };
        // Publish the page reference first so a failed/retried content commit can never be orphaned.
        await Save(Current with { Namespaces = new(Current.Namespaces) { [@namespace] = metadata } });
        return Page(@namespace, metadata.Generation, page);
    }
    private IMemoryPage Page(string @namespace, int generation, int page) => GrainFactory.GetGrain<IMemoryPage>(PageId(Owner, @namespace, generation, page));
    internal static string PageId(string owner, string @namespace, int generation, int page) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Owner = owner, Namespace = @namespace, generation, page }))));
    private async Task ClearRetired(string @namespace)
    {
        var metadata = Current.Namespaces.GetValueOrDefault(@namespace);
        if (metadata is null) { return; }
        foreach (var (generation, pages) in metadata.RetiredPages.ToArray())
        {
            foreach (var page in pages) { await Page(@namespace, generation, page).Clear(); }
            var retired = new Dictionary<int, HashSet<int>>(metadata.RetiredPages);
            retired.Remove(generation);
            metadata = metadata with { RetiredPages = retired };
            await Save(Current with { Namespaces = new(Current.Namespaces) { [@namespace] = metadata } });
        }
    }
    private static MemoryKey RequireKey(string @namespace, string key)
    {
        if (string.IsNullOrWhiteSpace(@namespace) || string.IsNullOrWhiteSpace(key) || @namespace.Length > 512 || key.Length > 512)
        { throw new ArgumentException("Memory namespaces and keys require 1–512 non-blank characters."); }
        return new(@namespace, key);
    }
    private async Task Save(MemoryState next)
    {
        var old = state.State;
        state.State = next;
        try { await state.WriteStateAsync(); }
        catch { state.State = old; throw; }
    }
}
