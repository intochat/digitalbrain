using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Memory;
using DigitalBrain.Memory.Qdrant;
using DigitalBrain.Testing;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client;
using Xunit;

namespace DigitalBrain.Memory.Tests;

public sealed class QdrantVectorMemoryContract : IAsyncLifetime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static IContainer? SharedContainer;
    private static string? Endpoint;
    private static int Users;

    public async ValueTask InitializeAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Users++;
            if (SharedContainer is not null)
            {
                return;
            }

            SharedContainer = new ContainerBuilder("qdrant/qdrant:v1.15.1")
                .WithPortBinding(6334, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6334))
                .Build();

            await SharedContainer.StartAsync().ConfigureAwait(false);
            var port = SharedContainer.GetMappedPublicPort(6334);
            Endpoint = $"http://127.0.0.1:{port}";
        }
        finally
        {
            Gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Users--;
            if (Users == 0 && SharedContainer is not null)
            {
                await SharedContainer.DisposeAsync().ConfigureAwait(false);
                SharedContainer = null;
                Endpoint = null;
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task WithProviderAsync(
        string collectionName,
        Func<QdrantVectorMemoryProvider, CancellationToken, Task> body,
        CancellationToken cancellationToken)
    {
        using var client = new QdrantClient(new Uri(Endpoint!));
        await using var provider = new QdrantVectorMemoryProvider(client, collectionName);
        await body(provider, cancellationToken).ConfigureAwait(false);
    }

    [Fact(DisplayName = "Qdrant provider store then search returns entry by semantic rank")]
    public async Task Store_then_search_returns_entry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithProviderAsync("vm_store_search", async (provider, ct) =>
        {
            var embedding = DeterministicEmbeddingGenerator.Embed("alpha beta gamma");

            await provider.UpsertAsync(
                "owner-a",
                "notes",
                "k1",
                "alpha beta gamma",
                new Dictionary<string, string> { ["kind"] = "fact" },
                payload: null,
                embedding,
                ct);

            var hits = await provider.SearchAsync(
                "owner-a",
                "notes",
                DeterministicEmbeddingGenerator.Embed("alpha beta"),
                limit: 3,
                metadataFilter: null,
                ct);

            var hit = Assert.Single(hits);
            Assert.Equal("k1", hit.Key);
            Assert.Equal("alpha beta gamma", hit.Text);
            Assert.Equal("fact", hit.Metadata["kind"]);
        }, cancellationToken);
    }

    [Fact(DisplayName = "Qdrant provider search returns deterministic top-k order")]
    public async Task Search_returns_deterministic_top_k()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithProviderAsync("vm_top_k", async (provider, ct) =>
        {
            await provider.UpsertAsync("o", "notes", "near", "red apple fruit", Empty(), null, DeterministicEmbeddingGenerator.Embed("red apple fruit"), ct);
            await provider.UpsertAsync("o", "notes", "mid", "red apple", Empty(), null, DeterministicEmbeddingGenerator.Embed("red apple"), ct);
            await provider.UpsertAsync("o", "notes", "far", "blue ocean water", Empty(), null, DeterministicEmbeddingGenerator.Embed("blue ocean water"), ct);

            var hits = await provider.SearchAsync("o", "notes", DeterministicEmbeddingGenerator.Embed("red apple fruit"), 2, null, ct);
            Assert.Equal(2, hits.Count);
            Assert.Equal("near", hits[0].Key);
            Assert.Equal("mid", hits[1].Key);
        }, cancellationToken);
    }

    [Fact(DisplayName = "Qdrant provider search filters by metadata")]
    public async Task Search_filters_by_metadata()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithProviderAsync("vm_meta", async (provider, ct) =>
        {
            var shared = DeterministicEmbeddingGenerator.Embed("shared text");

            await provider.UpsertAsync("o", "notes", "a", "shared text", new Dictionary<string, string> { ["tag"] = "keep" }, null, shared, ct);
            await provider.UpsertAsync("o", "notes", "b", "shared text", new Dictionary<string, string> { ["tag"] = "drop" }, null, shared, ct);

            var hits = await provider.SearchAsync("o", "notes", shared, 5, new Dictionary<string, string> { ["tag"] = "keep" }, ct);
            var hit = Assert.Single(hits);
            Assert.Equal("a", hit.Key);
        }, cancellationToken);
    }

    [Fact(DisplayName = "Qdrant provider remove deletes entry")]
    public async Task Remove_deletes_entry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithProviderAsync("vm_remove", async (provider, ct) =>
        {
            var embedding = DeterministicEmbeddingGenerator.Embed("payload");

            await provider.UpsertAsync("o", "notes", "k1", "payload", Empty(), null, embedding, ct);
            Assert.True(await provider.RemoveAsync("o", "notes", "k1", ct));
            Assert.Empty(await provider.SearchAsync("o", "notes", embedding, 5, null, ct));
        }, cancellationToken);
    }

    [Fact(DisplayName = "Qdrant provider remove missing key is false")]
    public async Task Remove_missing_key_is_false()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithProviderAsync("vm_remove_missing", async (provider, ct) =>
        {
            Assert.False(await provider.RemoveAsync("o", "notes", "absent", ct));
        }, cancellationToken);
    }

    [Fact(DisplayName = "Qdrant provider store replaces existing key")]
    public async Task Store_replaces_existing_key()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithProviderAsync("vm_replace", async (provider, ct) =>
        {
            await provider.UpsertAsync("o", "notes", "k1", "old text", new Dictionary<string, string> { ["v"] = "1" }, null, DeterministicEmbeddingGenerator.Embed("old text"), ct);
            await provider.UpsertAsync("o", "notes", "k1", "new text", new Dictionary<string, string> { ["v"] = "2" }, null, DeterministicEmbeddingGenerator.Embed("new text"), ct);

            var hit = Assert.Single(await provider.SearchAsync("o", "notes", DeterministicEmbeddingGenerator.Embed("new text"), 5, null, ct));
            Assert.Equal("new text", hit.Text);
            Assert.Equal("2", hit.Metadata["v"]);
        }, cancellationToken);
    }

    [Fact(DisplayName = "Qdrant provider collection ensure is idempotent")]
    public async Task Collection_ensure_is_idempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithProviderAsync("vm_idempotent", async (provider, ct) =>
        {
            var embedding = DeterministicEmbeddingGenerator.Embed("once");

            await provider.UpsertAsync("o", "notes", "k1", "once", Empty(), null, embedding, ct);
            await provider.UpsertAsync("o", "notes", "k2", "twice", Empty(), null, DeterministicEmbeddingGenerator.Embed("twice"), ct);

            var hits = await provider.SearchAsync("o", "notes", embedding, 5, null, ct);
            Assert.Contains(hits, static hit => hit.Key == "k1");
        }, cancellationToken);
    }

    [Fact(DisplayName = "Qdrant provider rejects mismatched embedding dimensions")]
    public async Task Dimension_mismatch_is_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithProviderAsync("vm_dim", async (provider, ct) =>
        {
            await provider.UpsertAsync("o", "notes", "k1", "ok", Empty(), null, DeterministicEmbeddingGenerator.Embed("ok"), ct);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await provider.UpsertAsync("o", "notes", "k2", "bad", Empty(), null, new float[3], ct));
        }, cancellationToken);
    }

    [Fact(DisplayName = "Qdrant provider cancellation reaches the client")]
    public async Task Cancellation_reaches_client()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithProviderAsync("vm_cancel", async (provider, ct) =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await cts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await provider.UpsertAsync("o", "notes", "k1", "text", Empty(), null, DeterministicEmbeddingGenerator.Embed("text"), cts.Token));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await provider.SearchAsync("o", "notes", DeterministicEmbeddingGenerator.Embed("text"), 1, null, cts.Token));
        }, cancellationToken);
    }

    [Fact(DisplayName = "Qdrant provider failures do not surface Qdrant exception types publicly")]
    public async Task Failures_do_not_leak_qdrant_exception_types()
    {
        using var client = new QdrantClient(new Uri("http://127.0.0.1:1"));
        await using var provider = new QdrantVectorMemoryProvider(client, "vm_fail");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.UpsertAsync("o", "notes", "k1", "text", Empty(), null, DeterministicEmbeddingGenerator.Embed("text"), cts.Token));

        Assert.DoesNotContain("Qdrant", exception.GetType().FullName!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Grpc", exception.GetType().FullName!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Qdrant provider protected payload reference round-trips")]
    public async Task Protected_payload_round_trips()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithProviderAsync("vm_payload", async (provider, ct) =>
        {
            var payload = new ProtectedPayloadReference(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1));
            var embedding = DeterministicEmbeddingGenerator.Embed("text");

            await provider.UpsertAsync("o", "notes", "k1", "text", Empty(), payload, embedding, ct);
            var hit = Assert.Single(await provider.SearchAsync("o", "notes", embedding, 1, null, ct));
            Assert.Equal(payload, hit.Payload);
        }, cancellationToken);
    }

    [Fact(DisplayName = "Memory contracts assembly exposes no Qdrant types")]
    public void Contracts_expose_no_qdrant_types()
    {
        foreach (var type in typeof(IVectorMemory).Assembly.GetExportedTypes())
        {
            Assert.DoesNotContain("Qdrant", type.FullName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Qdrant", type.Namespace, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact(DisplayName = "Memory assembly keeps DigitalBrain.Memory.Qdrant public surface to registration only")]
    public void Memory_qdrant_namespace_exports_only_registration_surface()
    {
        var memoryAssembly = typeof(MemoryModule).Assembly;
        Assert.Equal("DigitalBrain.Modules.Memory", memoryAssembly.GetName().Name);
        Assert.Same(memoryAssembly, typeof(QdrantVectorMemoryRegistration).Assembly);

        var exportedFromQdrantNamespace = memoryAssembly
            .GetExportedTypes()
            .Where(static type => type.Namespace == "DigitalBrain.Memory.Qdrant")
            .Select(static type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([nameof(QdrantVectorMemoryRegistration)], exportedFromQdrantNamespace);
        Assert.DoesNotContain("QdrantVectorMemoryProvider", exportedFromQdrantNamespace);
        Assert.DoesNotContain("QdrantVectorMemoryHit", exportedFromQdrantNamespace);
        Assert.Null(typeof(QdrantVectorMemoryRegistration).GetMethod(
            "CreateClient",
            BindingFlags.Public | BindingFlags.Static));
    }

    [Fact(DisplayName = "WithQdrant is the public Aspire projection entry point")]
    public void WithQdrant_extension_exists_on_module_builder()
    {
        var method = typeof(DigitalBrain.Memory.Aspire.Hosting.MemoryHostingExtensions)
            .GetMethod(nameof(DigitalBrain.Memory.Aspire.Hosting.MemoryHostingExtensions.WithQdrant));
        Assert.NotNull(method);
        Assert.True(method!.IsStatic);
    }

    [Fact(DisplayName = "Qdrant-backed IVectorMemory neuron store/search/remove via fixture")]
    public async Task Neuron_contract_against_qdrant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var collection = "vm_neuron_" + Guid.NewGuid().ToString("N");
        await using var fixture = new QdrantMemoryFixture(Endpoint!, collection);
        await fixture.InitializeAsync();
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var memory = test.Client.Get<IVectorMemory>(MemoryFixture.Memory);
        var notes = new VectorMemoryNamespace("notes");

        var stored = await memory.SendAsync(
            new StoreVectorMemory(notes, "k1", "alpha beta gamma", new Dictionary<string, string> { ["kind"] = "fact" }, null),
            cancellationToken);
        Assert.True(stored.Stored);

        var search = await memory.SendAsync(new SearchVectorMemory(notes, "alpha beta", 3, null), cancellationToken);
        var match = Assert.Single(search.Matches);
        Assert.Equal("k1", match.Key);

        var removed = await memory.SendAsync(new RemoveVectorMemory(notes, "k1"), cancellationToken);
        Assert.True(removed.Removed);
    }

    private static IReadOnlyDictionary<string, string> Empty()
        => new Dictionary<string, string>();
}

internal sealed class QdrantMemoryFixture(string endpoint, string collectionName) : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.Configure(MemoryModule.ProviderConfigurationKey, MemoryModule.QdrantProviderName);
        brain.Configure(QdrantVectorMemoryRegistration.ConnectionNameConfigurationKey, "memory-qdrant");
        brain.Configure(QdrantVectorMemoryRegistration.CollectionNameConfigurationKey, collectionName);
        brain.Configure("ConnectionStrings:memory-qdrant", $"Endpoint={endpoint}");
        brain.AddModule<MemoryModule>();
        brain.ConfigureServiceEdge(
            static services => services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>, DeterministicEmbeddingGenerator>(),
            new object(),
            static _ => { });
    }
}
