using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Signals;
using DigitalBrain.Kernel;
using DigitalBrain.Qdrant;
using DigitalBrain.Registry;
using DigitalBrain.Sdk.Vectors;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Registry.Tests;

public sealed class TypeSearchFacts
{
    [Fact]
    public async Task SearchDegradesWithoutAComposedVectorStoreEvenWhenEmbeddingsAreAvailable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().WithModule<RegistryModule>().WithModule<RegistryFixtureModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new ClockEmbeddings()))
            .StartAsync(ct);
        var registry = brain.Get<IRegistry>(RegistryModule.Key);
        Assert.Contains(await registry.Types(), type => type.Id == "test.registry-clock");
        Assert.Empty(await registry.Search("wake me", 1, ct));
    }

    [Fact]
    public async Task ComposedQdrantSearchesTheRealVectorStore()
    {
        var connection = Environment.GetEnvironmentVariable("DIGITALBRAIN_QDRANT_TEST_CONNECTION");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(connection), "Set DIGITALBRAIN_QDRANT_TEST_CONNECTION for the real vector-store contract.");
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().WithModule<RegistryModule>().WithModule<QdrantModule>()
            .WithModule<RegistryFixtureModule>()
            .ConfigureSilo(silo => { silo.Configuration["ConnectionStrings:qdrant"] = connection; silo.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new ClockEmbeddings()); })
            .StartAsync(ct);
        var hit = Assert.Single(await brain.Get<IRegistry>(RegistryModule.Key).Search("wake me tomorrow", 1, ct));
        Assert.Equal("test.registry-clock", hit.Type.Id);
        Assert.Equal(1, hit.Score, precision: 4);
    }

    [Fact]
    public async Task VectorSearchReturnsTypesAndCanBeRebuiltAfterAnEmbeddingFailure()
    {
        var embedder = new ClockEmbeddings { Fail = true };
        await using var brain = await ModuleTest.Create().WithModule<RegistryModule>().WithModule<QdrantModule>()
            .WithModule<RegistryFixtureModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embedder))
            .StartAsync(TestContext.Current.CancellationToken);
        var registry = brain.Get<IRegistry>(RegistryModule.Key);

        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.Search("wake me tomorrow", 1, TestContext.Current.CancellationToken));
        Assert.Contains(await registry.Types(), type => type.Id == "test.registry-clock");
        embedder.Fail = false;
        var found = Assert.Single(await registry.Search("wake me tomorrow", 1, TestContext.Current.CancellationToken));
        Assert.Equal("test.registry-clock", found.Type.Id);
        Assert.Equal(1, found.Score, precision: 4);
        Assert.Contains("Ping", Assert.Single(found.Type.Methods), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SharedVectorStoreDoesNotLeakTypesFromAnotherComposition()
    {
        var vectors = new InMemoryQdrant();
        var ct = TestContext.Current.CancellationToken;
        await using (var first = await ModuleTest.Create().WithModule<RegistryModule>().WithModule<RegistryFixtureModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IVectorStore>(vectors)
                .AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new ClockEmbeddings())).StartAsync(ct))
        {
            Assert.Equal("test.registry-clock", Assert.Single(await first.Get<IRegistry>(RegistryModule.Key).Search("wake me", 1, ct)).Type.Id);
        }
        await using var second = await ModuleTest.Create().WithModule<RegistryModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IVectorStore>(vectors)
                .AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new ClockEmbeddings())).StartAsync(ct);

        Assert.DoesNotContain(await second.Get<IRegistry>(RegistryModule.Key).Search("wake me", 25, ct), hit => hit.Type.Id == "test.registry-clock");
    }

    [Fact]
    public async Task ModuleAnnouncementsDoNotChangeTheSelectedComposition()
    {
        await using var brain = await ModuleTest.Create().WithModule<RegistryModule>().StartAsync(TestContext.Current.CancellationToken);
        var registry = brain.Get<IRegistry>(RegistryModule.Key);
        Assert.DoesNotContain(await registry.Types(), type => type.Id == "test.registry-clock");
        var hub = brain.SiloServices.GetRequiredService<LocalSignalHub>();
        using var announcements = hub.Subscribe<ModuleLoaded>();
        var announced = new ModuleLoaded(typeof(RegistryFixtureModule).AssemblyQualifiedName!);
        hub.Publish(announced);
        Assert.Same(announced, await announcements.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.DoesNotContain(await registry.Types(), type => type.Id == "test.registry-clock");
    }

    private sealed class ClockEmbeddings : IEmbeddingGenerator<string, Embedding<float>>
    {
        public bool Fail { get; set; }
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
            => Fail ? throw new InvalidOperationException("embedding service unavailable")
                : Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(values.Select(text =>
                    new Embedding<float>(text.Contains("wake", StringComparison.OrdinalIgnoreCase) ? new float[] { 1, 0 } : [0, 1]))));
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
