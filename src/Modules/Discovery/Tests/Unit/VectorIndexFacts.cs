using DigitalBrain.Discovery;
using DigitalBrain.Discovery.Search;
using DigitalBrain.Discovery.Vector;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class VectorIndexFacts
{
    [Fact]
    public void CollectionsAreNamedForTheEmbeddingModel()
    {
        Assert.Equal("intochat_capabilities__hashing_v1", DiscoveryCollections.NameFor(CapabilityCollection.SystemCapabilities, "hashing-v1"));
        Assert.Equal("intochat_workspace_instances__hashing_v1", DiscoveryCollections.NameFor(CapabilityCollection.WorkspaceInstances, "hashing-v1"));
        Assert.Equal("intochat_user_memories__hashing_v1", DiscoveryCollections.NameFor(CapabilityCollection.UserMemories, "hashing-v1"));
    }

    [Fact]
    public async Task WorkspaceInstancesAreScopedAndSystemCapabilitiesAreGlobal()
    {
        var ct = TestContext.Current.CancellationToken;
        var embedder = new HashingCapabilityEmbedder();
        var index = new InMemoryCapabilityVectorIndex(embedder.ModelId);
        var query = await embedder.EmbedAsync("invoice schedule", ct);

        await index.UpsertAsync(
            CapabilityCollection.WorkspaceInstances,
            [
                new CapabilityVectorRecord("window-a", "workspace-a", "invoice schedule", query),
            ],
            ct);
        await index.UpsertAsync(
            CapabilityCollection.SystemCapabilities,
            [
                new CapabilityVectorRecord("intochat.invoice", string.Empty, "invoice schedule", query),
            ],
            ct);

        var scoped = await index.SearchAsync(CapabilityCollection.WorkspaceInstances, "workspace-b", query, 5, ct);
        Assert.Empty(scoped);

        var mine = await index.SearchAsync(CapabilityCollection.WorkspaceInstances, "workspace-a", query, 5, ct);
        Assert.Equal("window-a", Assert.Single(mine).Id);

        var global = await index.SearchAsync(CapabilityCollection.SystemCapabilities, "workspace-b", query, 5, ct);
        Assert.Equal("intochat.invoice", Assert.Single(global).Id);
    }
}
