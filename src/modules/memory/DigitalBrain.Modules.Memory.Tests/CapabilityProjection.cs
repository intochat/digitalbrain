using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Memory.Tests;

public sealed class CapabilityProjectionContract(MemoryFixture fixture)
{
    [Fact(DisplayName = "Active exact-catalog entries project into reserved capability namespace")]
    public async Task Active_catalog_projects_into_reserved_namespace()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var catalog = ActiveCapabilityCatalog.Create([GreeterModule()]);
        var store = new InMemoryVectorMemoryStore();
        using var embeddings = new DeterministicEmbeddingGenerator();
        var reconciler = new ProjectionReconciler(store, embeddings);

        var entries = CapabilityProjection.FromCatalog(catalog);
        var result = await reconciler.ReconcileAsync(
            "owner-a",
            VectorMemoryNamespace.Capabilities,
            entries,
            cancellationToken);

        Assert.Equal(VectorMemoryNamespace.Capabilities, result.Namespace);
        Assert.True(result.Upserted > 0);
        Assert.Equal(0, result.Removed);
        Assert.Contains(entries, entry => entry.Key == "harness.greeter");
        Assert.Contains(entries, entry => entry.Key == "harness.say-hello@v1");

        var embedding = DeterministicEmbeddingGenerator.Embed("greeter say hello");
        var matches = await store.SearchAsync(
            "owner-a",
            VectorMemoryNamespace.Capabilities.Value,
            embedding,
            limit: 10,
            metadataFilter: null,
            cancellationToken);

        Assert.Contains(matches, match => match.Key == "harness.greeter" || match.Key == "harness.say-hello@v1");
        Assert.All(matches, match =>
        {
            Assert.True(match.Metadata.ContainsKey(VectorProjectionMetadataKeys.ContractId)
                || match.Metadata.ContainsKey(VectorProjectionMetadataKeys.ModuleId));
            Assert.Null(match.Payload);
        });
    }

    [Fact(DisplayName = "NL-like retrieval returns projected synapse metadata that is candidate-shaped for exact validation")]
    public async Task Nl_like_retrieval_returns_candidate_shaped_synapse_metadata()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var catalog = ActiveCapabilityCatalog.Create([GreeterModule()]);
        var store = new InMemoryVectorMemoryStore();
        using var embeddings = new DeterministicEmbeddingGenerator();
        var reconciler = new ProjectionReconciler(store, embeddings);
        var entries = CapabilityProjection.FromCatalog(catalog);
        await reconciler.ReconcileAsync(
            "owner-a",
            VectorMemoryNamespace.Capabilities,
            entries,
            cancellationToken);

        var embedding = DeterministicEmbeddingGenerator.Embed("ask the greeter to say hello");
        var matches = await store.SearchAsync(
            "owner-a",
            VectorMemoryNamespace.Capabilities.Value,
            embedding,
            limit: 8,
            metadataFilter: null,
            cancellationToken);

        var synapse = Assert.Single(
            matches,
            match => match.Key == "harness.say-hello@v1"
                || (match.Metadata.GetValueOrDefault(VectorProjectionMetadataKeys.Kind)
                        == VectorProjectionKinds.Synapse
                    && match.Metadata.GetValueOrDefault(VectorProjectionMetadataKeys.ContractId)
                        == "harness.say-hello"));

        Assert.Equal(VectorProjectionKinds.Synapse, synapse.Metadata[VectorProjectionMetadataKeys.Kind]);
        Assert.Equal("harness.say-hello", synapse.Metadata[VectorProjectionMetadataKeys.ContractId]);
        Assert.Equal("1", synapse.Metadata[VectorProjectionMetadataKeys.SchemaVersion]);
        Assert.Equal("harness.greeter", synapse.Metadata[VectorProjectionMetadataKeys.NeuronContractId]);
        Assert.Equal(
            "digitalbrain.testing.greeter",
            synapse.Metadata[VectorProjectionMetadataKeys.ModuleId]);
        Assert.Null(synapse.Payload);

        Assert.True(catalog.TryGetSynapse("harness.say-hello", schemaVersion: 1, out var exact));
        Assert.Equal(1, exact!.SchemaVersion);
        Assert.Equal("harness.say-hello", exact.ContractId);
        Assert.False(catalog.TryGetSynapse("harness.say-hello", schemaVersion: 99, out _));
    }

    [Fact(DisplayName = "Capability projection rebuild is idempotent and removes stale inactive entries")]
    public async Task Rebuild_is_idempotent_and_removes_stale()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var greeter = GreeterModule();
        var probe = ProbeModule();
        var store = new InMemoryVectorMemoryStore();
        using var embeddings = new DeterministicEmbeddingGenerator();
        var reconciler = new ProjectionReconciler(store, embeddings);

        var first = CapabilityProjection.FromCatalog(ActiveCapabilityCatalog.Create([greeter, probe]));
        var firstResult = await reconciler.ReconcileAsync(
            "owner-a",
            VectorMemoryNamespace.Capabilities,
            first,
            cancellationToken);
        Assert.Equal(first.Count, firstResult.Upserted);
        Assert.Equal(first.Count, firstResult.ActiveKeys.Count);

        var secondResult = await reconciler.ReconcileAsync(
            "owner-a",
            VectorMemoryNamespace.Capabilities,
            first,
            cancellationToken);
        Assert.Equal(first.Count, secondResult.Upserted);
        Assert.Equal(0, secondResult.Removed);
        Assert.Equal(firstResult.ActiveKeys.Order(StringComparer.Ordinal), secondResult.ActiveKeys.Order(StringComparer.Ordinal));

        var onlyGreeter = CapabilityProjection.FromCatalog(ActiveCapabilityCatalog.Create([greeter]));
        var trimmed = await reconciler.ReconcileAsync(
            "owner-a",
            VectorMemoryNamespace.Capabilities,
            onlyGreeter,
            cancellationToken);

        Assert.Equal(onlyGreeter.Count, trimmed.Upserted);
        Assert.True(trimmed.Removed > 0);
        Assert.Equal(onlyGreeter.Count, trimmed.ActiveKeys.Count);
        Assert.DoesNotContain(trimmed.ActiveKeys, key => key.Contains("capability-caller", StringComparison.Ordinal)
            || key.Contains("capability-target", StringComparison.Ordinal)
            || key.Contains("capability-ping", StringComparison.Ordinal));

        var remaining = await store.ListKeysAsync(
            "owner-a",
            VectorMemoryNamespace.Capabilities.Value,
            cancellationToken);
        Assert.Equal(onlyGreeter.Select(static entry => entry.Key).Order(StringComparer.Ordinal), remaining.Order(StringComparer.Ordinal));
    }

    [Fact(DisplayName = "Vector candidates carry stable exact IDs and cannot override exact schema/version truth")]
    public async Task Candidates_carry_stable_ids_and_do_not_override_exact_catalog()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var catalog = ActiveCapabilityCatalog.Create([GreeterModule()]);
        var entries = CapabilityProjection.FromCatalog(catalog);
        var store = new InMemoryVectorMemoryStore();
        using var embeddings = new DeterministicEmbeddingGenerator();
        var reconciler = new ProjectionReconciler(store, embeddings);
        await reconciler.ReconcileAsync(
            "owner-a",
            VectorMemoryNamespace.Capabilities,
            entries,
            cancellationToken);

        var synapse = Assert.Single(entries, entry =>
            entry.Metadata.GetValueOrDefault(VectorProjectionMetadataKeys.Kind) == VectorProjectionKinds.Synapse
            && entry.Metadata.GetValueOrDefault(VectorProjectionMetadataKeys.ContractId) == "harness.say-hello");

        Assert.Equal("1", synapse.Metadata[VectorProjectionMetadataKeys.SchemaVersion]);
        Assert.Equal("harness.say-hello@v1", synapse.Key);
        Assert.DoesNotContain("\"type\"", synapse.Text, StringComparison.Ordinal);

        var poisoned = synapse with
        {
            Metadata = new Dictionary<string, string>(synapse.Metadata, StringComparer.Ordinal)
            {
                [VectorProjectionMetadataKeys.SchemaVersion] = "99",
                [VectorProjectionMetadataKeys.ContractId] = "forged.synapse",
            },
        };
        await reconciler.ReconcileAsync(
            "owner-a",
            VectorMemoryNamespace.Capabilities,
            [poisoned],
            cancellationToken);

        var embedding = DeterministicEmbeddingGenerator.Embed(poisoned.Text);
        var matches = await store.SearchAsync(
            "owner-a",
            VectorMemoryNamespace.Capabilities.Value,
            embedding,
            limit: 5,
            metadataFilter: null,
            cancellationToken);
        var hit = Assert.Single(matches);
        Assert.Equal("99", hit.Metadata[VectorProjectionMetadataKeys.SchemaVersion]);
        Assert.Equal("forged.synapse", hit.Metadata[VectorProjectionMetadataKeys.ContractId]);

        Assert.False(catalog.TryGetSynapse("forged.synapse", schemaVersion: 99, out _));
        Assert.True(catalog.TryGetSynapse("harness.say-hello", schemaVersion: 1, out var exact));
        Assert.Equal(1, exact!.SchemaVersion);
        Assert.Equal("harness.say-hello", exact.ContractId);
    }

    [Fact(DisplayName = "Capability projection text and metadata never carry secrets or protected payloads")]
    public void Projection_excludes_secrets_and_protected_payloads()
    {
        var catalog = ActiveCapabilityCatalog.Create([GreeterModule()]);
        var entries = CapabilityProjection.FromCatalog(catalog);

        Assert.NotEmpty(entries);
        Assert.All(entries, entry =>
        {
            Assert.DoesNotContain("secret", entry.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", entry.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", entry.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("payload", entry.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Bearer", entry.Text, StringComparison.Ordinal);
            foreach (var value in entry.Metadata.Values)
            {
                Assert.DoesNotContain("secret", value, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("token", value, StringComparison.OrdinalIgnoreCase);
            }
        });
    }

    [Fact(DisplayName = "Community store and remove stay blocked on reserved capability namespace after projection")]
    public async Task Community_cannot_write_or_remove_reserved_projection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var memory = test.Client.Get<IVectorMemory>(MemoryFixture.Memory);

        var stored = await memory.SendAsync(
            new StoreVectorMemory(
                VectorMemoryNamespace.Capabilities,
                "harness.greeter",
                "forged greeter capability",
                new Dictionary<string, string> { [VectorProjectionMetadataKeys.ContractId] = "harness.greeter" },
                Payload: null),
            cancellationToken);
        Assert.False(stored.Stored);
        Assert.Equal(VectorMemoryStoreStatus.ReservedNamespace, stored.Status);

        var removed = await memory.SendAsync(
            new RemoveVectorMemory(VectorMemoryNamespace.Capabilities, "harness.greeter"),
            cancellationToken);
        Assert.False(removed.Removed);
    }

    [Fact(DisplayName = "ProjectionBootNeuron handles DigitalBrainActivated and emits reconcile facts")]
    public void Projection_boot_neuron_is_activation_reconciler()
    {
        Assert.Contains(
            typeof(IHandle<DigitalBrainActivated>),
            typeof(ProjectionBootNeuron).GetInterfaces());
        Assert.Contains(
            typeof(IEmit<VectorProjectionReconciled>),
            typeof(ProjectionBootNeuron).GetInterfaces());
        Assert.Equal(
            "memory-projection-boot",
            NeuronId.GrainTypeNameOf(typeof(ProjectionBootNeuron)));

        var source = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "modules", "memory", "DigitalBrain.Modules.Memory", "MemoryModule.cs"));
        Assert.Contains(nameof(ProjectionReconciler), source, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }

    private static ICompiledModule GreeterModule()
    {
        var sayHello = new SynapseCapabilityDescriptor(
            "harness.say-hello",
            schemaVersion: 1,
            "Ask the greeter to say hello",
            """{"type":"object","properties":{"name":{"type":"string"}}}""",
            ["say hello to Alice"]);
        var greeted = new SynapseCapabilityDescriptor(
            "harness.greeted",
            schemaVersion: 1,
            "Greeter responded",
            """{"type":"object","properties":{"message":{"type":"string"}}}""",
            []);
        var neuron = new NeuronCapabilityDescriptor(
            "harness.greeter",
            "Greeter neuron",
            "default",
            [sayHello],
            [greeted]);
        return new ScriptedModule(
            new ModuleId("digitalbrain.testing.greeter"),
            new CapabilityManifest(
                new ModuleId("digitalbrain.testing.greeter"),
                "1.0.0",
                "Testing greeter module",
                [],
                [neuron]));
    }

    private static ICompiledModule ProbeModule()
    {
        var ping = new SynapseCapabilityDescriptor(
            "db.testing.capability-ping",
            schemaVersion: 1,
            "Capability probe ping",
            """{"type":"object"}""",
            []);
        var caller = new NeuronCapabilityDescriptor(
            "testing.capability-caller",
            "Capability caller neuron",
            "default",
            [ping],
            []);
        var target = new NeuronCapabilityDescriptor(
            "testing.capability-target",
            "Capability target neuron",
            "default",
            [ping],
            []);
        return new ScriptedModule(
            new ModuleId("digitalbrain.testing.capability-probe"),
            new CapabilityManifest(
                new ModuleId("digitalbrain.testing.capability-probe"),
                "1.0.0",
                "Capability probe module",
                [],
                [caller, target]));
    }
}
