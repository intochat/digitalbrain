using System.Security.Cryptography;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Qdrant.Client;

namespace DigitalBrain.Product.Memory.Qdrant.Tests;

public sealed class QdrantMemoryStoreContractTests : IAsyncLifetime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static IContainer? sharedContainer;
    private static string? endpoint;
    private static int users;

    public async ValueTask InitializeAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            users++;
            if (sharedContainer is not null)
            {
                return;
            }

            sharedContainer = new ContainerBuilder("qdrant/qdrant:v1.18.0")
                .WithPortBinding(6334, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6334))
                .Build();
            await sharedContainer.StartAsync().ConfigureAwait(false);
            endpoint = $"http://127.0.0.1:{sharedContainer.GetMappedPublicPort(6334)}";
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
            users--;
            if (users == 0 && sharedContainer is not null)
            {
                await sharedContainer.DisposeAsync().ConfigureAwait(false);
                sharedContainer = null;
                endpoint = null;
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    [Fact(Explicit = true)]
    public async Task UpsertSearchAndFiltersConvergeOnOneLogicalEntry()
    {
        await WithFactoryAsync(async factory =>
        {
            var store = factory.CreateForWorkspace("workspace/memory-acme");
            await store.StoreAsync(
                new MemoryEntry(
                    "acme-funding",
                    "Acme closed a Series B funding round.",
                    new Dictionary<string, string> { ["account"] = "acme", ["source"] = "gmail" }),
                Cancellation);
            await store.StoreAsync(
                new MemoryEntry(
                    "acme-funding",
                    "Acme closed an updated Series B funding round.",
                    new Dictionary<string, string> { ["account"] = "acme", ["source"] = "gmail", ["revision"] = "2" }),
                Cancellation);
            await store.StoreAsync(
                new MemoryEntry(
                    "acme-web",
                    "Web coverage of Acme's funding round.",
                    new Dictionary<string, string> { ["account"] = "acme", ["source"] = "web" }),
                Cancellation);

            var hits = await store.SearchAsync(
                new MemoryQuery(
                    "Acme funding round",
                    5,
                    new Dictionary<string, string>
                    {
                        ["account"] = "acme",
                        ["source"] = "gmail",
                    }),
                Cancellation);

            var hit = Assert.Single(hits);
            Assert.Equal("acme-funding", hit.Entry.Id);
            Assert.Equal("Acme closed an updated Series B funding round.", hit.Entry.Content);
            Assert.Equal("2", hit.Entry.Metadata["revision"]);
            Assert.True(double.IsFinite(hit.Score));
            Assert.InRange(hit.Score, 0d, 1d);
        });
    }

    [Fact(Explicit = true)]
    public async Task WorkspaceIsolationAndScopedRemovalHoldForTheSameEntryId()
    {
        await WithFactoryAsync(async factory =>
        {
            var left = factory.CreateForWorkspace("workspace/memory-left");
            var right = factory.CreateForWorkspace(" workspace/memory-left ");
            const string sharedEntryId = "shared-entry";
            await left.StoreAsync(
                new MemoryEntry(
                    sharedEntryId,
                    "Left workspace account evidence.",
                    new Dictionary<string, string> { ["account"] = "left" }),
                Cancellation);

            Assert.Empty(await right.SearchAsync(new MemoryQuery("Left evidence", 5), Cancellation));
            await right.RemoveAsync(sharedEntryId, Cancellation);
            await right.RemoveAsync(sharedEntryId, Cancellation);

            var leftHits = await left.SearchAsync(new MemoryQuery("Left evidence", 5), Cancellation);
            Assert.Equal(sharedEntryId, Assert.Single(leftHits).Entry.Id);

            await right.StoreAsync(
                new MemoryEntry(
                    sharedEntryId,
                    "Right workspace account evidence.",
                    new Dictionary<string, string> { ["account"] = "right" }),
                Cancellation);
            Assert.Equal(sharedEntryId, Assert.Single(await left.SearchAsync(new MemoryQuery("Left evidence", 5), Cancellation)).Entry.Id);
            Assert.Equal(sharedEntryId, Assert.Single(await right.SearchAsync(new MemoryQuery("Right evidence", 5), Cancellation)).Entry.Id);
        });
    }

    [Fact(Explicit = true)]
    public async Task EmbeddingDimensionMismatchDoesNotCorruptExistingMemory()
    {
        await WithFactoryAsync(async (factory, client, options) =>
        {
            var store = factory.CreateForWorkspace("workspace/memory-dimensions");
            await store.StoreAsync(
                new MemoryEntry("dimension-ok", "Stable dimensional memory.", new Dictionary<string, string>()),
                Cancellation);

            using var mismatchedFactory = new QdrantMemoryStoreFactory(
                client,
                new DeterministicEmbedder(7),
                options);
            var mismatched = mismatchedFactory.CreateForWorkspace("workspace/memory-dimensions");
            await Assert.ThrowsAsync<InvalidOperationException>(() => mismatched.StoreAsync(
                new MemoryEntry("dimension-bad", "This vector has a different size.", new Dictionary<string, string>()),
                Cancellation));

            Assert.Equal(
                "dimension-ok",
                Assert.Single(await store.SearchAsync(new MemoryQuery("Stable memory", 5), Cancellation)).Entry.Id);
        });
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static async Task WithFactoryAsync(Func<QdrantMemoryStoreFactory, Task> body)
        => await WithFactoryAsync(async (factory, _, _) => await body(factory).ConfigureAwait(false)).ConfigureAwait(false);

    private static async Task WithFactoryAsync(Func<QdrantMemoryStoreFactory, QdrantClient, QdrantMemoryOptions, Task> body)
    {
        using var client = new QdrantClient(new Uri(endpoint ?? throw new InvalidOperationException("Qdrant endpoint is unavailable.")));
        var options = new QdrantMemoryOptions(
            "memory_" + Guid.NewGuid().ToString("N"),
            "test-memory-isolation-secret");
        using var factory = new QdrantMemoryStoreFactory(client, new DeterministicEmbedder(16), options);
        await body(factory, client, options).ConfigureAwait(false);
    }

    private sealed class DeterministicEmbedder(int dimensions) : ITextEmbeddingGenerator
    {
        public Task<MemoryEmbedding> EmbedAsync(string text, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(text);
            cancellationToken.ThrowIfCancellationRequested();

            var vector = new float[dimensions];
            foreach (var token in text.Split([' ', '.', ',', '\'', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token.ToUpperInvariant()));
                var index = BitConverter.ToUInt32(hash, 0) % (uint)vector.Length;
                vector[index] += 1f;
            }

            var magnitude = MathF.Sqrt(vector.Sum(value => value * value));
            if (magnitude > 0f)
            {
                for (var index = 0; index < vector.Length; index++)
                {
                    vector[index] /= magnitude;
                }
            }

            return Task.FromResult(new MemoryEmbedding(vector));
        }
    }
}
