using DigitalBrain.Apps;
using DigitalBrain.Discovery;
using DigitalBrain.Discovery.Search;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CatalogFacts
{
    [Fact]
    public async Task SearchReturnsCapabilityIdsFromTheManifestAndResolvesAliases()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start(new FixtureManifestSource([InvoiceManifest()]), ct);
        var catalog = brain.Get<ICapabilityCatalog>("catalog");

        var result = await catalog.Search("summarize my outstanding invoices", "workspace-a", 5);

        Assert.Equal("intochat.invoices/summarize_invoices", result.Hits[0].Id);
        Assert.Equal(CapabilityKind.Operation, result.Hits[0].Kind);
        Assert.False(result.Degraded);

        var alias = await catalog.Search("intochat.invoices", "workspace-a", 5);
        Assert.Equal("intochat.invoices", Assert.Single(alias.Hits).Id);
    }

    [Fact]
    public async Task EmbeddingsDownFallsBackToKeywordAndFlagsDegraded()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start(new FixtureManifestSource([InvoiceManifest()]), ct, new FailingEmbedder());
        var catalog = brain.Get<ICapabilityCatalog>("catalog");

        var result = await catalog.Search("summarize my outstanding invoices", "workspace-a", 5);

        Assert.True(result.Degraded);
        Assert.Equal("intochat.invoices/summarize_invoices", result.Hits[0].Id);
    }

    [Fact]
    public async Task UnmetIntentIsRecordedAndGrouped()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start(new FixtureManifestSource([InvoiceManifest()]), ct);
        var catalog = brain.Get<ICapabilityCatalog>("catalog");

        await catalog.Search("zzzpuddle", "workspace-a", 5);
        await catalog.Search("zzzpuddle", "workspace-a", 5);

        var items = await brain.Get<IUnmetIntentBoard>("unmet-intents").Read();
        var single = Assert.Single(items);
        Assert.Equal("zzzpuddle", single.Text);
        Assert.Equal("workspace-a", single.WorkspaceId);
        Assert.Equal(2, single.Count);
    }

    [Fact]
    public async Task VectorSearchRunsOnlyWhenCandidatesExceedEight()
    {
        var ct = TestContext.Current.CancellationToken;
        var embedder = new CountingEmbedder();
        await using var brain = await Start(new FixtureManifestSource(BroadManifests()), ct, embedder);
        var catalog = brain.Get<ICapabilityCatalog>("catalog");

        await catalog.Search("report", "workspace-a", 5);
        var callsAfterBroadSearch = embedder.Calls;

        await catalog.Search("nightingale", "workspace-a", 5);

        Assert.Equal(callsAfterBroadSearch, embedder.Calls);
        await catalog.Search("report", "workspace-a", 5);
        Assert.Equal(callsAfterBroadSearch + 1, embedder.Calls);
    }

    private static Task<UnitBrain> Start(IManifestSource source, CancellationToken ct, ICapabilityEmbedder? embedder = null)
    {
        var builder = UnitTest.Create().WithModule<DiscoveryModule>()
            .ConfigureSilo(silo =>
            {
                silo.Services.AddSingleton(source);
                if (embedder is not null)
                {
                    silo.Services.AddSingleton(embedder);
                }
            });
        return builder.StartAsync(ct);
    }

    internal static AppManifest InvoiceManifest() => new()
    {
        Id = "intochat.invoices",
        Version = "1.0.0",
        Publisher = "intochat",
        Kind = AppKind.Declarative,
        Name = "Invoice Desk",
        DescriptionForPeople = "Track and summarize invoices.",
        DescriptionForModel = "Invoices and payment schedules.",
        Operations =
        [
            new AppOperation
            {
                Name = "summarize_invoices",
                DescriptionForModel = "Summarize outstanding invoices into a payment schedule.",
                ReadOnly = true,
            },
        ],
    };

    private static IReadOnlyList<AppManifest> BroadManifests()
    {
        var operations = Enumerable.Range(0, 12)
            .Select(index => new AppOperation
            {
                Name = $"report_{index}",
                DescriptionForModel = $"Build report number {index} for the team.",
                ReadOnly = true,
            })
            .Append(new AppOperation
            {
                Name = "nightingale",
                DescriptionForModel = "Sing the nightingale song.",
                ReadOnly = true,
            })
            .ToArray();
        return
        [
            new AppManifest
            {
                Id = "intochat.reports",
                Version = "1.0.0",
                Publisher = "intochat",
                Kind = AppKind.Declarative,
                Name = "Report Studio",
                DescriptionForPeople = "Reports.",
                DescriptionForModel = "Build reports.",
                Operations = operations,
            },
        ];
    }
}

internal sealed class FixtureManifestSource(IReadOnlyList<AppManifest> manifests) : IManifestSource
{
    public Task<IReadOnlyList<AppManifest>> ReadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(manifests);
}

internal sealed class FailingEmbedder : ICapabilityEmbedder
{
    public string ModelId => "failing";

    public ValueTask<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("embeddings are down");
}

internal sealed class CountingEmbedder : ICapabilityEmbedder
{
    private readonly HashingCapabilityEmbedder _inner = new();

    public int Calls { get; private set; }

    public string ModelId => _inner.ModelId;

    public ValueTask<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        Calls++;
        return _inner.EmbedAsync(text, cancellationToken);
    }
}
