using Xunit;

namespace DigitalBrain.Memory.Tests;

public sealed class BehaviorProjectionContract(MemoryFixture fixture)
{
    [Fact(DisplayName = "Published behavior descriptions and scenarios become searchable")]
    public async Task Published_behaviors_are_searchable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new InMemoryVectorMemoryStore();
        using var embeddings = new DeterministicEmbeddingGenerator();
        var reconciler = new ProjectionReconciler(store, embeddings);
        var sources = new[]
        {
            new BehaviorProjectionSource(
                BehaviorId: "behavior.account-enrichment",
                DisplayName: "Account enrichment",
                Description: "Enrich a Salesforce account from a Gmail message.",
                ScenarioTitles: ["enrich account from email", "skip private drafts"],
                ArtifactHash: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                Visibility: BehaviorProjectionVisibility.Published),
        };

        var entries = BehaviorProjection.FromSources(sources);
        var result = await reconciler.ReconcileAsync(
            "owner-a",
            VectorMemoryNamespace.Behaviors,
            entries,
            cancellationToken);

        Assert.Equal(1, result.Upserted);
        Assert.Equal(["behavior.account-enrichment"], result.ActiveKeys);

        var embedding = DeterministicEmbeddingGenerator.Embed("enrich account from email salesforce gmail");
        var matches = await store.SearchAsync(
            "owner-a",
            VectorMemoryNamespace.Behaviors.Value,
            embedding,
            limit: 5,
            metadataFilter: null,
            cancellationToken);

        var match = Assert.Single(matches);
        Assert.Equal("behavior.account-enrichment", match.Key);
        Assert.Contains("Account enrichment", match.Text, StringComparison.Ordinal);
        Assert.Contains("enrich account from email", match.Text, StringComparison.Ordinal);
        Assert.Equal(VectorProjectionKinds.Behavior, match.Metadata[VectorProjectionMetadataKeys.Kind]);
        Assert.Equal("behavior.account-enrichment", match.Metadata[VectorProjectionMetadataKeys.BehaviorId]);
        Assert.Equal(
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            match.Metadata[VectorProjectionMetadataKeys.ArtifactHash]);
        Assert.Null(match.Payload);
    }

    [Fact(DisplayName = "Draft stopped and private behaviors obey visibility policy and are not projected")]
    public async Task Draft_stopped_private_are_not_searchable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new InMemoryVectorMemoryStore();
        using var embeddings = new DeterministicEmbeddingGenerator();
        var reconciler = new ProjectionReconciler(store, embeddings);
        var sources = new[]
        {
            Source("behavior.published", BehaviorProjectionVisibility.Published, "published enrichment"),
            Source("behavior.draft", BehaviorProjectionVisibility.Draft, "draft enrichment"),
            Source("behavior.private", BehaviorProjectionVisibility.Private, "private enrichment"),
            Source("behavior.stopped", BehaviorProjectionVisibility.Stopped, "stopped enrichment"),
        };

        var entries = BehaviorProjection.FromSources(sources);
        Assert.Single(entries);
        Assert.Equal("behavior.published", entries[0].Key);

        await reconciler.ReconcileAsync(
            "owner-a",
            VectorMemoryNamespace.Behaviors,
            entries,
            cancellationToken);

        // Stale draft that was previously written must be removed on reconcile.
        await store.UpsertAsync(
            new VectorMemoryEntry(
                "owner-a",
                VectorMemoryNamespace.Behaviors.Value,
                "behavior.draft",
                "stale draft enrichment",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [VectorProjectionMetadataKeys.Kind] = VectorProjectionKinds.Behavior,
                    [VectorProjectionMetadataKeys.BehaviorId] = "behavior.draft",
                },
                Payload: null,
                DeterministicEmbeddingGenerator.Embed("stale draft enrichment")),
            cancellationToken);

        var rebuilt = await reconciler.ReconcileAsync(
            "owner-a",
            VectorMemoryNamespace.Behaviors,
            entries,
            cancellationToken);
        Assert.Equal(1, rebuilt.Removed);
        Assert.Equal(["behavior.published"], rebuilt.ActiveKeys);

        var embedding = DeterministicEmbeddingGenerator.Embed("enrichment");
        var matches = await store.SearchAsync(
            "owner-a",
            VectorMemoryNamespace.Behaviors.Value,
            embedding,
            limit: 10,
            metadataFilter: null,
            cancellationToken);

        Assert.All(matches, match => Assert.Equal("behavior.published", match.Key));
        Assert.DoesNotContain(matches, match => match.Key is "behavior.draft" or "behavior.private" or "behavior.stopped");
    }

    [Fact(DisplayName = "Behavior projection rebuild is idempotent")]
    public async Task Behavior_rebuild_is_idempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new InMemoryVectorMemoryStore();
        using var embeddings = new DeterministicEmbeddingGenerator();
        var reconciler = new ProjectionReconciler(store, embeddings);
        var entries = BehaviorProjection.FromSources(
        [
            Source("behavior.one", BehaviorProjectionVisibility.Published, "one"),
            Source("behavior.two", BehaviorProjectionVisibility.Published, "two"),
        ]);

        var first = await reconciler.ReconcileAsync(
            "owner-a",
            VectorMemoryNamespace.Behaviors,
            entries,
            cancellationToken);
        var second = await reconciler.ReconcileAsync(
            "owner-a",
            VectorMemoryNamespace.Behaviors,
            entries,
            cancellationToken);

        Assert.Equal(first.ActiveKeys.Order(StringComparer.Ordinal), second.ActiveKeys.Order(StringComparer.Ordinal));
        Assert.Equal(0, second.Removed);
        Assert.Equal(2, second.Upserted);
    }

    [Fact(DisplayName = "Behavior projection never embeds secrets or protected payload references")]
    public void Behavior_projection_excludes_secrets_and_payloads()
    {
        var entries = BehaviorProjection.FromSources(
        [
            new BehaviorProjectionSource(
                BehaviorId: "behavior.safe",
                DisplayName: "Safe",
                Description: "Public description only",
                ScenarioTitles: ["public scenario"],
                ArtifactHash: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                Visibility: BehaviorProjectionVisibility.Published),
        ]);

        var entry = Assert.Single(entries);
        Assert.DoesNotContain("secret", entry.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", entry.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payload", entry.Text, StringComparison.OrdinalIgnoreCase);
        Assert.False(entry.Metadata.ContainsKey("payload"));
        Assert.False(entry.Metadata.ContainsKey("protected_payload"));
    }

    [Fact(DisplayName = "Community cannot store or remove reserved behavior projection entries")]
    public async Task Community_cannot_mutate_reserved_behavior_namespace()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var memory = test.Client.Get<IVectorMemory>(MemoryFixture.Memory);

        var stored = await memory.SendAsync(
            new StoreVectorMemory(
                VectorMemoryNamespace.Behaviors,
                "behavior.forged",
                "forged published behavior",
                new Dictionary<string, string>
                {
                    [VectorProjectionMetadataKeys.BehaviorId] = "behavior.forged",
                },
                Payload: null),
            cancellationToken);
        Assert.False(stored.Stored);
        Assert.Equal(VectorMemoryStoreStatus.ReservedNamespace, stored.Status);

        var removed = await memory.SendAsync(
            new RemoveVectorMemory(VectorMemoryNamespace.Behaviors, "behavior.forged"),
            cancellationToken);
        Assert.False(removed.Removed);
    }

    private static BehaviorProjectionSource Source(
        string behaviorId,
        BehaviorProjectionVisibility visibility,
        string description)
        => new(
            BehaviorId: behaviorId,
            DisplayName: behaviorId,
            Description: description,
            ScenarioTitles: [description],
            ArtifactHash: "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
            Visibility: visibility);
}
