using DigitalBrain.Memory;
using DigitalBrain.Qdrant;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class VectorMemoryStoreFacts
{
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
    }
}
