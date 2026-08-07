using DigitalBrain.Abstractions;
using DigitalBrain.Memory;
using Xunit;

namespace DigitalBrain.Memory.Tests;

public sealed class VectorMemoryContract(MemoryFixture fixture)
{
    [Fact(DisplayName = "IVectorMemory is a marker INeuron with no declared members")]
    public void Marker_is_INeuron_with_no_declared_members()
    {
        Assert.True(typeof(INeuron).IsAssignableFrom(typeof(IVectorMemory)));
        Assert.Empty(typeof(IVectorMemory).GetMethods().Where(static method => method.DeclaringType == typeof(IVectorMemory)));
        Assert.Empty(typeof(IVectorMemory).GetProperties().Where(static property => property.DeclaringType == typeof(IVectorMemory)));
    }

    [Fact(DisplayName = "Store then search returns the entry by semantic rank")]
    public async Task Store_then_search_returns_entry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var memory = test.Client.Get<IVectorMemory>(MemoryFixture.Memory);
        var notes = new VectorMemoryNamespace("notes");

        var stored = await memory.SendAsync(
            new StoreVectorMemory(
                notes,
                "k1",
                "alpha beta gamma",
                new Dictionary<string, string> { ["kind"] = "fact" },
                Payload: null),
            cancellationToken);

        Assert.True(stored.Stored);
        Assert.Equal(notes, stored.Namespace);
        Assert.Equal("k1", stored.Key);
        Assert.Equal(VectorMemoryStoreStatus.Stored, stored.Status);

        var search = await memory.SendAsync(
            new SearchVectorMemory(notes, "alpha beta", Limit: 3, Metadata: null),
            cancellationToken);

        Assert.Equal(notes, search.Namespace);
        var match = Assert.Single(search.Matches);
        Assert.Equal("k1", match.Key);
        Assert.Equal("alpha beta gamma", match.Text);
        Assert.Equal("fact", match.Metadata["kind"]);
        Assert.Null(match.Payload);
    }

    [Fact(DisplayName = "Search returns deterministic top-k ordered by similarity")]
    public async Task Search_returns_deterministic_top_k()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var memory = test.Client.Get<IVectorMemory>(MemoryFixture.Memory);
        var notes = new VectorMemoryNamespace("notes");

        await memory.SendAsync(new StoreVectorMemory(notes, "near", "red apple fruit", EmptyMetadata(), null), cancellationToken);
        await memory.SendAsync(new StoreVectorMemory(notes, "mid", "red apple", EmptyMetadata(), null), cancellationToken);
        await memory.SendAsync(new StoreVectorMemory(notes, "far", "blue ocean water", EmptyMetadata(), null), cancellationToken);

        var search = await memory.SendAsync(
            new SearchVectorMemory(notes, "red apple fruit", Limit: 2, Metadata: null),
            cancellationToken);

        Assert.Equal(2, search.Matches.Count);
        Assert.Equal("near", search.Matches[0].Key);
        Assert.Equal("mid", search.Matches[1].Key);
    }

    [Fact(DisplayName = "Search filters matches by required metadata")]
    public async Task Search_filters_by_metadata()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var memory = test.Client.Get<IVectorMemory>(MemoryFixture.Memory);
        var notes = new VectorMemoryNamespace("notes");

        await memory.SendAsync(
            new StoreVectorMemory(notes, "a", "shared text", new Dictionary<string, string> { ["tag"] = "keep" }, null),
            cancellationToken);
        await memory.SendAsync(
            new StoreVectorMemory(notes, "b", "shared text", new Dictionary<string, string> { ["tag"] = "drop" }, null),
            cancellationToken);

        var search = await memory.SendAsync(
            new SearchVectorMemory(notes, "shared text", Limit: 5, new Dictionary<string, string> { ["tag"] = "keep" }),
            cancellationToken);

        var match = Assert.Single(search.Matches);
        Assert.Equal("a", match.Key);
    }

    [Fact(DisplayName = "Remove deletes an entry so subsequent search misses it")]
    public async Task Remove_deletes_entry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var memory = test.Client.Get<IVectorMemory>(MemoryFixture.Memory);
        var notes = new VectorMemoryNamespace("notes");

        await memory.SendAsync(new StoreVectorMemory(notes, "k1", "payload", EmptyMetadata(), null), cancellationToken);

        var removed = await memory.SendAsync(new RemoveVectorMemory(notes, "k1"), cancellationToken);
        Assert.True(removed.Removed);
        Assert.Equal(notes, removed.Namespace);
        Assert.Equal("k1", removed.Key);

        var search = await memory.SendAsync(
            new SearchVectorMemory(notes, "payload", Limit: 5, Metadata: null),
            cancellationToken);
        Assert.Empty(search.Matches);
    }

    [Fact(DisplayName = "Remove of a missing key reports not removed")]
    public async Task Remove_missing_key_is_false()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var memory = test.Client.Get<IVectorMemory>(MemoryFixture.Memory);

        var removed = await memory.SendAsync(
            new RemoveVectorMemory(new VectorMemoryNamespace("notes"), "absent"),
            cancellationToken);

        Assert.False(removed.Removed);
    }

    [Fact(DisplayName = "Store with the same key replaces prior text and metadata")]
    public async Task Store_replaces_existing_key()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var memory = test.Client.Get<IVectorMemory>(MemoryFixture.Memory);
        var notes = new VectorMemoryNamespace("notes");

        await memory.SendAsync(
            new StoreVectorMemory(notes, "k1", "old text", new Dictionary<string, string> { ["v"] = "1" }, null),
            cancellationToken);
        await memory.SendAsync(
            new StoreVectorMemory(notes, "k1", "new text", new Dictionary<string, string> { ["v"] = "2" }, null),
            cancellationToken);

        var search = await memory.SendAsync(
            new SearchVectorMemory(notes, "new text", Limit: 5, Metadata: null),
            cancellationToken);

        var match = Assert.Single(search.Matches);
        Assert.Equal("new text", match.Text);
        Assert.Equal("2", match.Metadata["v"]);
    }

    [Fact(DisplayName = "Cancelled store and search honor caller cancellation")]
    public async Task Cancellation_is_honored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var memory = test.Client.Get<IVectorMemory>(MemoryFixture.Memory);
        var notes = new VectorMemoryNamespace("notes");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await memory.SendAsync(
                new StoreVectorMemory(notes, "k1", "text", EmptyMetadata(), null),
                cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await memory.SendAsync(
                new SearchVectorMemory(notes, "text", Limit: 1, Metadata: null),
                cts.Token));
    }

    [Fact(DisplayName = "Mutating search-result metadata does not alter stored entry or filtering")]
    public async Task Search_result_metadata_is_isolated_from_store()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var memory = test.Client.Get<IVectorMemory>(MemoryFixture.Memory);
        var notes = new VectorMemoryNamespace("notes");
        var inputMetadata = new Dictionary<string, string> { ["tag"] = "keep" };

        await memory.SendAsync(
            new StoreVectorMemory(notes, "k1", "shared text", inputMetadata, null),
            cancellationToken);

        inputMetadata["tag"] = "caller-mutated-input";

        var firstSearch = await memory.SendAsync(
            new SearchVectorMemory(notes, "shared text", Limit: 5, Metadata: null),
            cancellationToken);
        var firstMatch = Assert.Single(firstSearch.Matches);
        Assert.Equal("keep", firstMatch.Metadata["tag"]);

        var returnedMetadata = Assert.IsAssignableFrom<IDictionary<string, string>>(firstMatch.Metadata);
        returnedMetadata["tag"] = "mutated-via-search-result";

        var filtered = await memory.SendAsync(
            new SearchVectorMemory(notes, "shared text", Limit: 5, new Dictionary<string, string> { ["tag"] = "keep" }),
            cancellationToken);
        var filteredMatch = Assert.Single(filtered.Matches);
        Assert.Equal("k1", filteredMatch.Key);
        Assert.Equal("keep", filteredMatch.Metadata["tag"]);

        var secondSearch = await memory.SendAsync(
            new SearchVectorMemory(notes, "shared text", Limit: 5, Metadata: null),
            cancellationToken);
        var secondMatch = Assert.Single(secondSearch.Matches);
        Assert.Equal("keep", secondMatch.Metadata["tag"]);
    }

    [Fact(DisplayName = "Reserved capability and behavior namespaces reject store")]
    public async Task Reserved_namespaces_reject_store()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var memory = test.Client.Get<IVectorMemory>(MemoryFixture.Memory);

        var capabilities = await memory.SendAsync(
            new StoreVectorMemory(VectorMemoryNamespace.Capabilities, "k1", "x", EmptyMetadata(), null),
            cancellationToken);
        Assert.False(capabilities.Stored);
        Assert.Equal(VectorMemoryStoreStatus.ReservedNamespace, capabilities.Status);

        var behaviors = await memory.SendAsync(
            new StoreVectorMemory(VectorMemoryNamespace.Behaviors, "k1", "x", EmptyMetadata(), null),
            cancellationToken);
        Assert.False(behaviors.Stored);
        Assert.Equal(VectorMemoryStoreStatus.ReservedNamespace, behaviors.Status);
    }

    [Fact(DisplayName = "Store and search round-trip optional protected payload reference")]
    public async Task Protected_payload_reference_round_trips()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var memory = test.Client.Get<IVectorMemory>(MemoryFixture.Memory);
        var notes = new VectorMemoryNamespace("notes");
        var payload = new ProtectedPayloadReference(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1));

        await memory.SendAsync(
            new StoreVectorMemory(notes, "k1", "text", EmptyMetadata(), payload),
            cancellationToken);

        var search = await memory.SendAsync(
            new SearchVectorMemory(notes, "text", Limit: 1, Metadata: null),
            cancellationToken);

        var match = Assert.Single(search.Matches);
        Assert.Equal(payload, match.Payload);
    }

    [Fact(DisplayName = "Public contracts expose no provider, distance, embedding, Qdrant, or graph types")]
    public void Public_surface_hides_provider_details()
    {
        var publicTypes = typeof(IVectorMemory).Assembly.GetExportedTypes()
            .Concat(typeof(MemoryModule).Assembly.GetExportedTypes()
                .Where(static type => type.Namespace != "DigitalBrain.Memory.Qdrant"));

        foreach (var type in publicTypes)
        {
            Assert.DoesNotContain("Qdrant", type.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Distance", type.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Embedding", type.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Provider", type.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Graph", type.Name, StringComparison.OrdinalIgnoreCase);

            foreach (var property in type.GetProperties())
            {
                Assert.DoesNotContain("Distance", property.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Embedding", property.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Qdrant", property.Name, StringComparison.OrdinalIgnoreCase);
                Assert.False(
                    property.PropertyType == typeof(float[])
                    || property.PropertyType == typeof(ReadOnlyMemory<float>)
                    || property.PropertyType == typeof(IReadOnlyList<float>),
                    $"Public property '{type.Name}.{property.Name}' exposes raw vector data.");
            }
        }
    }

    private static IReadOnlyDictionary<string, string> EmptyMetadata()
        => new Dictionary<string, string>();
}
