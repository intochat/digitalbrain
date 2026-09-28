using DigitalBrain.Memory;
using DigitalBrain.Memory.Signals;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class MemoryFacts
{
    [Fact]
    public async Task HashCollisionsUseOverflowPagesAndPurgeRetriesIndexFailure()
    {
        var ct = TestContext.Current.CancellationToken;
        var index = new InMemoryVectorMemoryStore();
        await using var brain = await Start(index, ct);
        var memory = brain.Get<IMemory>("owner");
        var keys = Enumerable.Range(0, 100000).Select(value => "collision-" + value)
            .Where(key => System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key))[0] == 0)
            .Take(65).ToArray();
        Assert.Equal(65, keys.Length);
        foreach (var key in keys) { await memory.Remember(new("notes", key, key, [], null)); }
        Assert.Equal(65, (await memory.RebuildIndex("notes")).Indexed);
        index.FailWrites = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => memory.PurgeNamespace(new("notes")));
        Assert.Empty((await memory.Recall(new("notes", "test", 5, []))).Matches);
        index.FailWrites = false;
        Assert.Equal(0, (await memory.RebuildIndex("notes")).Pending);
        Assert.Empty(await index.SearchAsync("owner", "notes", [1, 1], 5, null, ct));
    }
    [Fact]
    public async Task CanonicalMemorySurvivesMissingIndexAndReactivationAndRebuildRetries()
    {
        var ct = TestContext.Current.CancellationToken;
        var index = new InMemoryVectorMemoryStore { FailWrites = true };
        await using var brain = await Start(index, ct);
        var memory = brain.Get<IMemory>("owner");
        await memory.Remember(new("notes", "stable", "durable text", [new("tag", "kept")], null));
        var failed = await memory.RebuildIndex("notes");
        Assert.Equal(1, failed.Pending);
        var shard = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("stable"))[0];
        await brain.DeactivateAsync(brain.Get<IMemoryPage>(MemoryNeuron.PageId("owner", "notes", 0, shard)), ct);
        await brain.DeactivateAsync(memory, ct);
        Assert.Equal("durable text", Assert.Single((await memory.Recall(new("notes", "durable", 5, []))).Matches).Text);
        index.FailWrites = false;
        Assert.Equal(0, (await memory.RebuildIndex("notes")).Pending);
        Assert.Single(await index.SearchAsync("owner", "notes", [1, 1], 5, null, ct));
        Assert.Empty((await brain.Get<IMemory>("other").Recall(new("notes", "durable", 5, []))).Matches);
    }

    [Fact]
    public async Task LegacyImportIsPagedScopedIdempotentAndDoesNotRestoreDeletedOrOverwriteNewerNotes()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = new InMemoryVectorMemoryStore();
        await source.UpsertAsync(new("owner", "notes", "a", "legacy a", [new("tag", "kept")], new(Guid.NewGuid().ToString(), null), [1, 1]), ct);
        await source.UpsertAsync(new("owner", "notes", "b", "legacy b", [], null, [1, 1]), ct);
        await source.UpsertAsync(new("someone-else", "notes", "secret", "private", [], null, [1, 1]), ct);
        await using var brain = await Start(source, ct);
        var memory = brain.Get<IMemory>("owner");
        var first = await memory.ImportLegacy("notes", limit: 1);
        Assert.Equal(1, first.Imported);
        Assert.NotNull(first.NextCursor);
        Assert.Equal(0, (await memory.ImportLegacy("notes", limit: 1)).Imported);
        await memory.Remember(new("notes", "b", "newer b", [], null));
        Assert.Equal(0, (await memory.ImportLegacy("notes", first.NextCursor, 1)).Imported);
        await memory.Forget(new("notes", "a"));
        Assert.Equal(0, (await memory.ImportLegacy("notes", limit: 1)).Imported);
        Assert.Equal("newer b", Assert.Single((await memory.Recall(new("notes", "text", 5, []))).Matches).Text);
        Assert.Equal(2, (await source.SearchAsync("owner", "notes", [1, 1], 5, null, ct)).Count);
        await memory.PurgeNamespace(new("notes"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => memory.ImportLegacy("notes"));
    }

    [Fact]
    public async Task MemoryWorksWithoutAnyQdrantRegistration()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<MemoryModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new FakeEmbeddings())).StartAsync(ct);
        var memory = brain.Get<IMemory>("owner");
        await memory.Remember(new("notes", "key", "stored", [], null));
        Assert.Single((await memory.Recall(new("notes", "stored", 5, []))).Matches);
        Assert.False((await memory.RebuildIndex("notes")).Available);
    }
    [Fact]
    public async Task RememberStoresRecallableNoteAndPublishes()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryVectorMemoryStore();
        await using var brain = await Start(store, ct);
        var memory = brain.Get<IMemory>("owner");
        await using var remembered = await brain.Observe<Remembered>(memory, ct);

        var key = await memory.Remember(new(
            "notes", "greeting", "hello world", [new MemoryTag("topic", "greeting")], null));

        Assert.Equal(new MemoryKey("notes", "greeting"), key);
        var published = await remembered.NextAsync(ct: ct);
        Assert.Equal("greeting", published.Key.Key);

        var recall = await memory.Recall(new("notes", "hello", 5, []));
        var match = Assert.Single(recall.Matches);
        Assert.Equal("greeting", match.Key);
        Assert.Equal("hello world", match.Text);
    }

    [Fact]
    public async Task RecallFiltersByTag()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start(new InMemoryVectorMemoryStore(), ct);
        var memory = brain.Get<IMemory>("owner");
        await memory.Remember(new("notes", "a", "alpha", [new MemoryTag("topic", "greeting")], null));
        await memory.Remember(new("notes", "b", "beta", [new MemoryTag("topic", "shopping")], null));

        var recall = await memory.Recall(new("notes", "query", 5, [new MemoryTag("topic", "shopping")]));

        var match = Assert.Single(recall.Matches);
        Assert.Equal("b", match.Key);
    }

    [Fact]
    public async Task ForgetRemovesNoteAndPublishes()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryVectorMemoryStore();
        await using var brain = await Start(store, ct);
        var memory = brain.Get<IMemory>("owner");
        await memory.Remember(new("notes", "greeting", "hello", [], null));
        await using var forgotten = await brain.Observe<Forgotten>(memory, ct);

        var key = await memory.Forget(new("notes", "greeting"));

        Assert.Equal(new MemoryKey("notes", "greeting"), key);
        var published = await forgotten.NextAsync(ct: ct);
        Assert.Equal("greeting", published.Key.Key);
        Assert.Empty((await memory.Recall(new("notes", "hello", 5, []))).Matches);
    }

    [Fact]
    public async Task PurgeNamespaceRemovesEveryNoteInThatNamespace()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryVectorMemoryStore();
        await using var brain = await Start(store, ct);
        var memory = brain.Get<IMemory>("owner");
        await memory.Remember(new("intochat.workspace", "a", "alpha", [], null));
        await memory.Remember(new("intochat.workspace", "b", "beta", [], null));
        await memory.Remember(new("other", "c", "gamma", [], null));
        await using var purged = await brain.Observe<NamespacePurged>(memory, ct);

        var removed = await memory.PurgeNamespace(new("intochat.workspace"));

        Assert.Equal(2, removed);
        var published = await purged.NextAsync(ct: ct);
        Assert.Equal("intochat.workspace", published.Namespace);
        Assert.Empty((await memory.Recall(new("intochat.workspace", "alpha", 5, []))).Matches);
        Assert.Single((await memory.Recall(new("other", "gamma", 5, []))).Matches);
    }

    [Fact]
    public async Task InvalidArgumentsAreRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start(new InMemoryVectorMemoryStore(), ct);
        var memory = brain.Get<IMemory>("owner");

        await Assert.ThrowsAsync<ArgumentException>(() => memory.Remember(new("", "key", "text", [], null)));
        await Assert.ThrowsAsync<ArgumentException>(() => memory.Remember(new("notes", "key", "", [], null)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => memory.Recall(new("notes", "query", 0, [])));
    }

    private static async Task<UnitBrain> Start(InMemoryVectorMemoryStore store, CancellationToken ct)
        => await UnitTest.Create().WithModule<MemoryModule>()
            .ConfigureSilo(silo =>
            {
                silo.Services.AddSingleton<IVectorMemoryStore>(store);
                silo.Services.AddSingleton<ILegacyVectorMemoryStore>(store);
                silo.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new FakeEmbeddings());
            })
            .StartAsync(ct);
}

internal sealed class FakeEmbeddings : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var embeddings = values
            .Select(static value => new Embedding<float>(new[] { value.Length, 1f }))
            .ToList();
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

internal sealed class InMemoryVectorMemoryStore : IVectorMemoryStore, ILegacyVectorMemoryStore
{
    private readonly Dictionary<(string Owner, string Namespace, string Key), VectorMemoryEntry> _entries = [];
    internal bool FailWrites { get; set; }

    public Task UpsertAsync(VectorMemoryEntry entry, CancellationToken cancellationToken)
    {
        if (FailWrites) { throw new IOException("Index unavailable"); }
        _entries[(entry.Name, entry.Namespace, entry.Key)] = entry;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RecalledMemory>> SearchAsync(
        string name,
        string @namespace,
        float[] queryEmbedding,
        int limit,
        IReadOnlyDictionary<string, string>? metadataFilter,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RecalledMemory> matches = _entries.Values
            .Where(entry => entry.Name == name && entry.Namespace == @namespace)
            .Where(entry => metadataFilter is null || metadataFilter.Count == 0
                || metadataFilter.All(filter => entry.Tags.Any(tag => tag.Name == filter.Key && tag.Value == filter.Value)))
            .Take(limit)
            .Select(static entry => new RecalledMemory(entry.Key, entry.Text, entry.Tags, entry.Payload))
            .ToArray();
        return Task.FromResult(matches);
    }

    public Task<bool> RemoveAsync(string name, string @namespace, string key, CancellationToken cancellationToken)
        => Task.FromResult(_entries.Remove((name, @namespace, key)));

    public Task<long> RemoveNamespaceAsync(string name, string @namespace, CancellationToken cancellationToken)
    {
        if (FailWrites) { throw new IOException("Index unavailable"); }
        var keys = _entries.Keys.Where(entry => entry.Owner == name && entry.Namespace == @namespace).ToArray();
        foreach (var key in keys) { _entries.Remove(key); }
        return Task.FromResult((long)keys.Length);
    }

    public Task<LegacyMemoryPage> ReadPage(string name, string @namespace, string? cursor, int limit, CancellationToken ct)
    {
        var offset = cursor is null ? 0 : int.Parse(cursor, System.Globalization.CultureInfo.InvariantCulture);
        var entries = _entries.Values.Where(entry => entry.Name == name && entry.Namespace == @namespace).OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
        return Task.FromResult(new LegacyMemoryPage(entries.Skip(offset).Take(limit).ToArray(), offset + limit < entries.Length ? (offset + limit).ToString(System.Globalization.CultureInfo.InvariantCulture) : null));
    }
}
