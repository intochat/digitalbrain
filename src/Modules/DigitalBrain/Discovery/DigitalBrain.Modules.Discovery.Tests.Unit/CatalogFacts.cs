using DigitalBrain.Apps;
using DigitalBrain.Discovery;
using DigitalBrain.Qdrant;
using DigitalBrain.Core.Registry;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Hosting;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Modules.Discovery.Tests.Unit;

public sealed class CatalogFacts
{
    [Fact]
    public async Task SearchIncludesPublicNeuronContractsFromSelectedModules()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<QdrantModule>().WithModule<DiscoveryModule>()
            .WithModule<FixtureNeuronModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<ICapabilitySource>(
                new FixtureManifestSource([ScopedAppManifest.Global(InvoiceManifest())])))
            .StartAsync(ct);

        var catalog = brain.Get<ICapabilityCatalog>("catalog");
        var neuron = await catalog.Search("emit a registry signal", "workspace-a", 5);
        Assert.Contains(neuron.Hits, hit => hit.Id == "test.registry-emitter/EmitRegistrySignal" && hit.Kind == CapabilityKind.Neuron);
        var emitter = Assert.Single(neuron.Hits, hit => hit.Id == "test.registry-emitter/EmitRegistrySignal");
        Assert.Equal("RegistryEmitter.EmitRegistrySignal", emitter.Name);
        Assert.DoesNotContain(neuron.Hits, hit => hit.Id == "test.registry-emitter");
        Assert.Contains("Emit Registry Signal", emitter.Description, StringComparison.Ordinal);

        var monitor = await catalog.Search("registry monitor", "workspace-a", 5);
        Assert.Contains(monitor.Hits, hit => hit.Id == "test.registry-monitor/MonitorRegistry");

        var app = await catalog.Search("summarize my outstanding invoices", "workspace-a", 5);
        Assert.Contains(app.Hits, hit => hit.Kind == CapabilityKind.Operation);
    }

    [Fact]
    public async Task DiscoveryReadsLocalRegistry()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<QdrantModule>().WithModule<DiscoveryModule>()
            .WithModule<FixtureNeuronModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<ICapabilitySource>(new FixtureManifestSource([])))
            .StartAsync(ct);
        var registry = brain.SiloServices.GetRequiredService<NeuronRegistry>();
        Assert.Equal(typeof(IRegistryEmitter), registry.Find("test.registry-emitter")?.Interface);

        var catalog = brain.Get<ICapabilityCatalog>("catalog");
        var result = await catalog.Search("emit a registry signal", "workspace-a", 5);
        Assert.Contains(result.Hits, hit => hit.Id == "test.registry-emitter/EmitRegistrySignal");
    }

    [Fact]
    public async Task ManifestSourceFailureKeepsRegistrySearchAvailable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<QdrantModule>().WithModule<DiscoveryModule>()
            .WithModule<FixtureNeuronModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<ICapabilitySource>(new ThrowingManifestSource()))
            .StartAsync(ct);

        var result = await brain.Get<ICapabilityCatalog>("catalog")
            .Search("emit a registry signal", "workspace-a", 5);

        Assert.True(result.Degraded);
        Assert.Contains(result.Hits, hit => hit.Id == "test.registry-emitter/EmitRegistrySignal");
    }

    [Fact]
    public async Task ManifestFailureAfterWarmupRetainsExistingAppIndex()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = new SwitchableManifestSource([ScopedAppManifest.Global(InvoiceManifest())]);
        await using var brain = await UnitTest.Create().WithModule<QdrantModule>().WithModule<DiscoveryModule>()
            .WithModule<FixtureNeuronModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<ICapabilitySource>(source))
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
        await using var brain = await UnitTest.Create().WithModule<QdrantModule>().WithModule<DiscoveryModule>()
            .WithModule<FixtureNeuronModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<ICapabilitySource>(source))
            .StartAsync(ct);
        var catalog = brain.Get<ICapabilityCatalog>("catalog");
        Assert.Contains((await catalog.Search("emit a registry signal", "workspace-a", 5)).Hits,
            hit => hit.Id == "test.registry-emitter/EmitRegistrySignal");

        source.Fail = false;
        var recovered = await catalog.Search("summarize outstanding invoices", "workspace-a", 5);
        Assert.Contains(recovered.Hits, hit => hit.Id == "intochat.invoices/summarize_invoices");
        Assert.False(recovered.Degraded);
    }

    [Fact]
    public async Task AppOnlySearchDoesNotLoseAppsToNeuronResultLimit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<QdrantModule>().WithModule<DiscoveryModule>()
            .WithModule<CrowdingNeuronModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<ICapabilitySource>(
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
    public async Task HitsCarryTheToolsTheirSourceDeclared()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start(new ToolSource(), ct);

        var result = await brain.Get<ICapabilityCatalog>("catalog").SearchApps("find new dental clinics", "workspace-a", 5);

        Assert.Equal(["propose_app", "run_leadgenerator"], Assert.Single(result.Hits).Tools);
    }

    [Fact]
    public async Task ASourceThatReportsAChangeIsReadAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = new ChangingSource();
        await using var brain = await Start(source, ct);
        var catalog = brain.Get<ICapabilityCatalog>("catalog");
        Assert.Empty((await catalog.Search("vendor onboarding", "workspace-a", 5)).Hits);

        await source.Add(new("intochat.vendor-onboarding", CapabilityKind.App, "Vendor Onboarding", "Onboard a new vendor."));

        Assert.Contains((await catalog.Search("vendor onboarding", "workspace-a", 5)).Hits, hit => hit.Id == "intochat.vendor-onboarding");
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
    public async Task VectorsFindACapabilityThatSharesNoWordWithTheQuery()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start(new FixtureManifestSource([ScopedAppManifest.Global(InvoiceManifest())]), ct, new SynonymEmbedder());
        var catalog = brain.Get<ICapabilityCatalog>("catalog");

        var result = await catalog.Search("what do my clients still owe", "workspace-a", 5);

        Assert.Contains(result.Hits, hit => hit.Id == "intochat.invoices/summarize_invoices");
        Assert.False(result.Degraded);
    }

    private static Task<UnitBrain> Start(ICapabilitySource source, CancellationToken ct, IEmbeddingGenerator<string, Embedding<float>>? embedder = null)
    {
        var builder = UnitTest.Create().WithModule<QdrantModule>().WithModule<DiscoveryModule>()
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
}

[Alias("test.registry-emitter")]
public interface IRegistryEmitter : INeuron { Task EmitRegistrySignal(); }
[Alias("test.registry-monitor")]
public interface IRegistryMonitor : INeuron { Task MonitorRegistry(); }

public sealed class FixtureNeuronModule : IModule
{
    public void Configure(ISiloBuilder silo) { }
}

public sealed class CrowdingNeuronModule : IModule
{
    public void Configure(ISiloBuilder silo) { }
}

internal sealed class ThrowingManifestSource : ICapabilitySource
{
    public Task<IReadOnlyList<CapabilityDocument>> Read(CancellationToken cancellationToken)
        => throw new InvalidOperationException("manifest source unavailable");
}

internal sealed class SwitchableManifestSource(IReadOnlyList<ScopedAppManifest> manifests) : ICapabilitySource
{
    public bool Fail { get; set; }
    public Task<IReadOnlyList<CapabilityDocument>> Read(CancellationToken cancellationToken)
        => Fail ? throw new InvalidOperationException("manifest source unavailable") : Task.FromResult(AppDocuments.Describe(manifests));
}

internal sealed class FixtureManifestSource(IReadOnlyList<ScopedAppManifest> manifests) : ICapabilitySource
{
    public Task<IReadOnlyList<CapabilityDocument>> Read(CancellationToken cancellationToken)
        => Task.FromResult(AppDocuments.Describe(manifests));
}

internal sealed class CountingManifestSource(IReadOnlyList<ScopedAppManifest> manifests) : ICapabilitySource
{
    private int reads;

    public int Reads => reads;

    public Task<IReadOnlyList<CapabilityDocument>> Read(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref reads);
        return Task.FromResult(AppDocuments.Describe(manifests));
    }
}

// Mirrors the Apps module's source: an app document plus one document per operation.
internal static class AppDocuments
{
    public static IReadOnlyList<CapabilityDocument> Describe(IEnumerable<ScopedAppManifest> manifests)
        => [.. manifests.SelectMany(static scoped => scoped.Manifest.Operations
            .Select(operation => new CapabilityDocument(scoped.Manifest.Id + "/" + operation.Name, CapabilityKind.Operation,
                operation.Name, operation.DescriptionForModel, scoped.OwningWorkspaceId))
            .Prepend(new CapabilityDocument(scoped.Manifest.Id, CapabilityKind.App, scoped.Manifest.Name,
                scoped.Manifest.DescriptionForPeople + " " + scoped.Manifest.DescriptionForModel, scoped.OwningWorkspaceId)))];
}

internal sealed class ChangingSource : ICapabilitySource
{
    private readonly List<CapabilityDocument> _documents = [];
    private readonly TaskCompletionSource<Action> _watcher = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task Add(CapabilityDocument document)
    {
        _documents.Add(document);
        (await _watcher.Task)();
    }

    public Task<IReadOnlyList<CapabilityDocument>> Read(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CapabilityDocument>>([.. _documents]);

    public Task Watch(Action changed, CancellationToken cancellationToken)
    {
        _watcher.TrySetResult(changed);
        return Task.Delay(Timeout.Infinite, cancellationToken);
    }
}

internal sealed class ToolSource : ICapabilitySource
{
    public Task<IReadOnlyList<CapabilityDocument>> Read(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CapabilityDocument>>([new("intochat.leadgenerator", CapabilityKind.App, "LeadGenerator",
            "Find new companies such as dental clinics.", Tools: ["propose_app", "run_leadgenerator"])]);
}

internal sealed class FailingEmbedder : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("embeddings are down");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

// Texts about money owed land on one axis, everything else on another.
internal sealed class SynonymEmbedder : IEmbeddingGenerator<string, Embedding<float>>
{
    private static readonly string[] MoneyOwed = ["invoice", "owe", "payment"];

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(values.Select(static text =>
            new Embedding<float>(MoneyOwed.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)) ? new float[] { 1, 0 } : [0, 1]))));

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
