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
        await using var brain = await Start(new FixtureManifestSource([ScopedAppManifest.Global(InvoiceManifest())]), ct);
        var catalog = brain.Get<ICapabilityCatalog>("catalog");

        var result = await catalog.Search("summarize my outstanding invoices", "workspace-a", 5);

        Assert.Equal("intochat.invoices/summarize_invoices", result.Hits[0].Id);
        Assert.Equal(CapabilityKind.Operation, result.Hits[0].Kind);
        Assert.False(result.Degraded);

        var alias = await catalog.Search("intochat.invoices", "workspace-a", 5);
        Assert.Equal("intochat.invoices", Assert.Single(alias.Hits).Id);
    }

    [Fact]
    public async Task SearchesReuseTheBuiltCatalogUntilAManifestChangeInvalidatesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = new CountingManifestSource([ScopedAppManifest.Global(InvoiceManifest())]);
        await using var brain = await Start(source, ct);
        var catalog = brain.Get<ICapabilityCatalog>("catalog");

        await catalog.Search("summarize my outstanding invoices", "workspace-a", 5);
        var readsAfterFirstSearch = source.Reads;
        Assert.True(readsAfterFirstSearch > 0, "the first search must build the catalog");

        // A search is a read of the built index, not a manifest re-read.
        await catalog.Search("summarize my outstanding invoices", "workspace-a", 5);
        await catalog.Search("intochat.invoices", "workspace-a", 5);
        Assert.Equal(readsAfterFirstSearch, source.Reads);

        brain.SiloServices.GetRequiredService<CapabilityCatalog>().Invalidate();
        await catalog.Search("summarize my outstanding invoices", "workspace-a", 5);
        Assert.True(source.Reads > readsAfterFirstSearch, "an invalidated catalog must re-read the manifest source");
    }

    [Fact]
    public async Task SavedAppIsSearchableOnlyFromItsWorkspaceWhileFirstPartyStaysGlobal()
    {
        var ct = TestContext.Current.CancellationToken;
        var saved = SavedManifest();
        await using var brain = await Start(
            new FixtureManifestSource(
            [
                ScopedAppManifest.Global(InvoiceManifest()),
                ScopedAppManifest.InWorkspace(saved, "workspace-a"),
            ]),
            ct);
        var catalog = brain.Get<ICapabilityCatalog>("catalog");

        var fromOwner = await catalog.Search("vendor onboarding intake", "workspace-a", 5);
        Assert.Contains(fromOwner.Hits, hit => hit.Id == "intochat.saved-vendor-intake");

        var fromStranger = await catalog.Search("vendor onboarding intake", "workspace-b", 5);
        Assert.DoesNotContain(fromStranger.Hits, hit => hit.Id == "intochat.saved-vendor-intake");

        var firstPartyFromStranger = await catalog.Search("summarize my outstanding invoices", "workspace-b", 5);
        Assert.Contains(firstPartyFromStranger.Hits, hit => hit.Id == "intochat.invoices/summarize_invoices");
    }

    [Fact]
    public async Task EmbeddingsDownFallsBackToKeywordAndFlagsDegraded()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start(new FixtureManifestSource([ScopedAppManifest.Global(InvoiceManifest())]), ct, new FailingEmbedder());
        var catalog = brain.Get<ICapabilityCatalog>("catalog");

        var result = await catalog.Search("summarize my outstanding invoices", "workspace-a", 5);

        Assert.True(result.Degraded);
        Assert.Equal("intochat.invoices/summarize_invoices", result.Hits[0].Id);
    }

    [Fact]
    public async Task UnmetIntentIsRecordedAndGrouped()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start(new FixtureManifestSource([ScopedAppManifest.Global(InvoiceManifest())]), ct);
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
        await using var brain = await Start(new FixtureManifestSource(Globals(BroadManifests())), ct, embedder);
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

    private static AppManifest SavedManifest() => new()
    {
        Id = "intochat.saved-vendor-intake",
        Version = "1.0.0",
        Publisher = "workspace",
        Kind = AppKind.Declarative,
        Name = "Vendor Onboarding Intake",
        DescriptionForPeople = "Enter a vendor onboarding intake form.",
        DescriptionForModel = "Open and submit the vendor onboarding intake.",
        Operations =
        [
            new AppOperation
            {
                Name = "Submit",
                DescriptionForModel = "Submit the vendor onboarding intake form.",
                ReadOnly = false,
            },
        ],
    };

    private static IReadOnlyList<ScopedAppManifest> Globals(IReadOnlyList<AppManifest> manifests)
        => [.. manifests.Select(ScopedAppManifest.Global)];

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

internal sealed class FixtureManifestSource(IReadOnlyList<ScopedAppManifest> manifests) : IManifestSource
{
    public Task<IReadOnlyList<ScopedAppManifest>> ReadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(manifests);
}

internal sealed class CountingManifestSource(IReadOnlyList<ScopedAppManifest> manifests) : IManifestSource
{
    private int reads;

    public int Reads => reads;

    public Task<IReadOnlyList<ScopedAppManifest>> ReadAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref reads);
        return Task.FromResult(manifests);
    }
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
