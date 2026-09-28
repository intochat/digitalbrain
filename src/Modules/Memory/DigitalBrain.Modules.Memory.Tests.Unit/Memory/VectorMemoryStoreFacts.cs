using DigitalBrain.Memory;
using DigitalBrain.Qdrant;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class VectorMemoryStoreFacts
{
    [Fact]
    public async Task EntriesRoundTripThroughTheQdrantProjection()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new VectorMemoryStore(new InMemoryQdrant(), null);
        var payload = new ProtectedPayloadReference(Guid.NewGuid().ToString("D"), DateTimeOffset.UnixEpoch);
        await store.UpsertAsync(new("owner", "notes", "a", "text a", [new("tag", "kept")], payload, [1, 0]), ct);
        await store.UpsertAsync(new("owner", "notes", "b", "text b", [], null, [0, 1]), ct);
        await store.UpsertAsync(new("someone-else", "notes", "secret", "private", [], null, [1, 1]), ct);

        var page = await store.ReadPage("owner", "notes", null, 10, ct);

        var first = Assert.Single(page.Entries, entry => entry.Key == "a");
        Assert.Equal("text a", first.Text);
        Assert.Equal([new MemoryTag("tag", "kept")], first.Tags);
        Assert.Equal(payload, first.Payload);
        Assert.Equal(2, page.Entries.Count);
    }

    [Fact]
    public async Task RemovalIsScopedToTheOwnerAndNamespace()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new VectorMemoryStore(new InMemoryQdrant(), null);
        await store.UpsertAsync(new("owner", "notes", "a", "text a", [], null, [1]), ct);
        await store.UpsertAsync(new("owner", "notes", "b", "text b", [], null, [1]), ct);
        await store.UpsertAsync(new("owner", "other", "c", "text c", [], null, [1]), ct);

        Assert.True(await store.RemoveAsync("owner", "notes", "a", ct));
        Assert.False(await store.RemoveAsync("owner", "notes", "a", ct));
        Assert.Equal(1, await store.RemoveNamespaceAsync("owner", "notes", ct));
        Assert.Single((await store.ReadPage("owner", "other", null, 10, ct)).Entries);
    }
}
