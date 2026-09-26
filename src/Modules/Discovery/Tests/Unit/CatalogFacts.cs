using DigitalBrain.Apps;
using DigitalBrain.Discovery;
using DigitalBrain.Discovery.Search;
using DigitalBrain.Core.Registry;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Registry;
using DigitalBrain.Core;
using Orleans.Hosting;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CatalogFacts
{
    [Fact]
    public async Task SearchIncludesOnlyRoutableNeuronContractsFromSelectedModules()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<DiscoveryModule>()
            .WithModule<FixtureNeuronModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IManifestSource>(
                new FixtureManifestSource([ScopedAppManifest.Global(InvoiceManifest())])))
            .StartAsync(ct);

        var catalog = brain.Get<ICapabilityCatalog>("catalog");
        var neuron = await catalog.Search("emit a registry signal", "workspace-a", 5);
        Assert.Contains(neuron.Hits, hit => hit.Id == "test.registry-emitter" && hit.Kind == CapabilityKind.Neuron);
        var details = await catalog.ReadNeuron("test.registry-emitter");
        Assert.Equal(typeof(IRegistryEmitter).FullName, details?.ContractType);
        Assert.Null(await catalog.ReadNeuron("test.registry-monitor"));

        var privateNeuron = await catalog.Search("hidden registry monitor", "workspace-a", 5);
        Assert.DoesNotContain(privateNeuron.Hits, hit => hit.Id == "test.registry-monitor");

        var app = await catalog.Search("summarize my outstanding invoices", "workspace-a", 5);
        Assert.Contains(app.Hits, hit => hit.Kind == CapabilityKind.Operation);
    }

    [Fact]
    public async Task DiscoveryReadsPublishedRegistry()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<DiscoveryModule>()
            .WithModule<FixtureNeuronModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IManifestSource>(new FixtureManifestSource([])))
            .StartAsync(ct);
        var selected = brain.SiloServices.GetRequiredService<INeuronRegistry>();
        var registry = brain.Get<INeuronRegistryGrain>(selected.Version);
        Assert.Contains(await registry.Read(), record => record.Id == "test.registry-emitter");

        var catalog = brain.Get<ICapabilityCatalog>("catalog");
        var result = await catalog.Search("emit a registry signal", "workspace-a", 5);
        Assert.Contains(result.Hits, hit => hit.Id == "test.registry-emitter");
        Assert.Equal(typeof(FixtureNeuronModule).FullName,
            (await catalog.ReadNeuron("test.registry-emitter"))?.ModuleId);
    }

    [Fact]
    public async Task ManifestSourceFailureKeepsRegistrySearchAvailable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<DiscoveryModule>()
            .WithModule<FixtureNeuronModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IManifestSource>(new ThrowingManifestSource()))
            .StartAsync(ct);

        var result = await brain.Get<ICapabilityCatalog>("catalog")
            .Search("emit a registry signal", "workspace-a", 5);

        Assert.True(result.Degraded);
        Assert.Contains(result.Hits, hit => hit.Id == "test.registry-emitter");
    }

    [Fact]
    public async Task ManifestFailureAfterWarmupRetainsExistingAppIndex()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = new SwitchableManifestSource([ScopedAppManifest.Global(InvoiceManifest())]);
        await using var brain = await UnitTest.Create().WithModule<DiscoveryModule>()
            .WithModule<FixtureNeuronModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IManifestSource>(source))
            .StartAsync(ct);
        var catalog = brain.Get<ICapabilityCatalog>("catalog");
        Assert.Contains((await catalog.Search("summarize invoices", "workspace-a", 5)).Hits,
            hit => hit.Id == "intochat.invoices/summarize_invoices");

        source.Fail = true;
        brain.SiloServices.GetRequiredService<CapabilityCatalog>().Invalidate();
        var result = await catalog.Search("summarize invoices", "workspace-a", 5);

        Assert.Contains(result.Hits, hit => hit.Id == "intochat.invoices/summarize_invoices");
        Assert.True(result.Degraded);

        source.Fail = false;
        var recovered = await catalog.Search("summarize invoices", "workspace-a", 5);
        Assert.False(recovered.Degraded);
    }

    [Fact]
    public async Task ManifestSourceRecoveryAfterStartupRestoresAppSearch()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = new SwitchableManifestSource([ScopedAppManifest.Global(InvoiceManifest())]) { Fail = true };
        await using var brain = await UnitTest.Create().WithModule<DiscoveryModule>()
            .WithModule<FixtureNeuronModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IManifestSource>(source))
            .StartAsync(ct);
        var catalog = brain.Get<ICapabilityCatalog>("catalog");
        Assert.Contains((await catalog.Search("emit a registry signal", "workspace-a", 5)).Hits,
            hit => hit.Id == "test.registry-emitter");

        source.Fail = false;
        var recovered = await catalog.Search("summarize outstanding invoices", "workspace-a", 5);
        Assert.Contains(recovered.Hits, hit => hit.Id == "intochat.invoices/summarize_invoices");
        Assert.False(recovered.Degraded);
    }

    [Fact]
    public async Task AppOnlySearchDoesNotLoseAppsToNeuronResultLimit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<DiscoveryModule>()
            .WithModule<CrowdingNeuronModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IManifestSource>(
                new FixtureManifestSource([ScopedAppManifest.Global(InvoiceManifest())])))
            .StartAsync(ct);

        var result = await brain.Get<ICapabilityCatalog>("catalog")
            .SearchApps("summarize outstanding invoices", "workspace-a", 5);
        Assert.Contains(result.Hits, hit => hit.Id == "intochat.invoices/summarize_invoices");
        Assert.All(result.Hits, hit => Assert.True(hit.Kind is CapabilityKind.App or CapabilityKind.Operation));
    }
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

public interface IRegistryEmitter : INeuron;
public interface IRegistryMonitor : INeuron;

public sealed class FixtureNeuronModule : IModule, INeuronRegistryContributor
{
    public IReadOnlyList<NeuronDescriptor> Neurons =>
    [
        new("test.registry-emitter", typeof(IRegistryEmitter), "Registry emitter", "Emit a registry signal", true),
        new("test.registry-monitor", typeof(IRegistryMonitor), "Registry monitor", "Hidden registry monitor", false),
    ];

    public void Configure(ISiloBuilder silo) { }
}

public sealed class CrowdingNeuronModule : IModule, INeuronRegistryContributor
{
    public IReadOnlyList<NeuronDescriptor> Neurons => Enumerable.Range(0, 10)
        .Select(index => new NeuronDescriptor($"test.invoice-neuron-{index}", typeof(IRegistryEmitter),
            $"Invoice neuron {index}", "Summarize outstanding invoices", true)).ToArray();
    public void Configure(ISiloBuilder silo) { }
}

internal sealed class ThrowingManifestSource : IManifestSource
{
    public Task<IReadOnlyList<ScopedAppManifest>> ReadAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("manifest source unavailable");
}

internal sealed class SwitchableManifestSource(IReadOnlyList<ScopedAppManifest> manifests) : IManifestSource
{
    public bool Fail { get; set; }
    public Task<IReadOnlyList<ScopedAppManifest>> ReadAsync(CancellationToken cancellationToken = default)
        => Fail ? throw new InvalidOperationException("manifest source unavailable") : Task.FromResult(manifests);
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
