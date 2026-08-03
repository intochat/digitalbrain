using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class AutomaticCapabilityDiscovery
{
    private const string NaturalLanguagePrompt = "exercise the lab harness connector for me";

    [Fact(DisplayName = "selecting an active test module makes its request synapse discoverable without AI code changes")]
    public async Task ActiveModuleRequestSynapseIsDiscoverable()
    {
        var catalog = ActiveCapabilityCatalog.Create([ProbeModule()]);
        var search = new ScriptedCandidateSearch(
        [
            new CapabilityCandidate(
                CapabilityKinds.Synapse,
                "moduletests.probe-request",
                SchemaVersion: 1,
                ModuleId: "digitalbrain.testing.capability-probe",
                NeuronContractId: "moduletests.probe-neuron",
                BehaviorId: null,
                ArtifactHash: null,
                SourceKey: "moduletests.probe-request@v1"),
        ]);
        var router = new CapabilityRouter(catalog, search);

        var selected = await router.SelectAsync(
            new OwnerId("owner-a"),
            "use the probe request capability",
            CancellationToken.None);

        var capability = Assert.Single(selected);
        Assert.Equal("moduletests.probe-request", capability.ContractId);
        Assert.Equal(1, capability.SchemaVersion);
        Assert.Equal("moduletests.probe-neuron", capability.NeuronContractId);
        Assert.Equal(ValidatedCapability.ToolNameFor("moduletests.probe-request", 1), capability.ToolName);
        Assert.Contains("probe request", capability.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "NL discovery without catalog terms requires semantic search; exact-term fallback alone is insufficient")]
    public async Task NaturalLanguageDiscoveryRequiresSemanticSearch()
    {
        var catalog = ActiveCapabilityCatalog.Create([ProbeModule()]);
        AssertNoExactTermOverlap(catalog, NaturalLanguagePrompt);

        var withoutSearch = new CapabilityRouter(catalog, search: null);
        Assert.Empty(await withoutSearch.SelectAsync(
            new OwnerId("owner-a"),
            NaturalLanguagePrompt,
            CancellationToken.None));

        var emptySearch = new RecordingCandidateSearch([]);
        var emptyRouter = new CapabilityRouter(catalog, emptySearch);
        Assert.Empty(await emptyRouter.SelectAsync(
            new OwnerId("owner-a"),
            NaturalLanguagePrompt,
            CancellationToken.None));
        Assert.Equal(1, emptySearch.CallCount);
        Assert.Equal(NaturalLanguagePrompt, emptySearch.LastPrompt);

        var search = new RecordingCandidateSearch(
        [
            CandidateFromProjectionShape(
                kind: CapabilityKinds.Synapse,
                contractId: "moduletests.probe-request",
                schemaVersion: 1,
                moduleId: "digitalbrain.testing.capability-probe",
                neuronContractId: "moduletests.probe-neuron",
                sourceKey: "moduletests.probe-request@v1"),
        ]);
        var router = new CapabilityRouter(catalog, search);

        var selected = await router.SelectAsync(
            new OwnerId("owner-a"),
            NaturalLanguagePrompt,
            CancellationToken.None);

        Assert.Equal(1, search.CallCount);
        Assert.Equal(NaturalLanguagePrompt, search.LastPrompt);
        Assert.Equal(new OwnerId("owner-a"), search.LastOwner);
        Assert.True(search.LastLimit >= 1);

        var capability = Assert.Single(selected);
        Assert.Equal("moduletests.probe-request", capability.ContractId);
        Assert.Equal(1, capability.SchemaVersion);
        Assert.Equal("moduletests.probe-neuron", capability.NeuronContractId);
        Assert.Equal(
            ValidatedCapability.ToolNameFor("moduletests.probe-request", 1),
            capability.ToolName);
        Assert.False(string.IsNullOrWhiteSpace(capability.JsonSchema));
        Assert.Contains("probe request", capability.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "semantic path is invoked when available even if exact terms would also match")]
    public async Task SemanticSearchIsAlwaysInvokedWhenAvailable()
    {
        var catalog = ActiveCapabilityCatalog.Create([ProbeModule()]);
        var search = new RecordingCandidateSearch(
        [
            CandidateFromProjectionShape(
                kind: CapabilityKinds.Synapse,
                contractId: "moduletests.probe-request",
                schemaVersion: 1,
                moduleId: "digitalbrain.testing.capability-probe",
                neuronContractId: "moduletests.probe-neuron",
                sourceKey: "moduletests.probe-request@v1"),
        ]);
        var router = new CapabilityRouter(catalog, search);

        var selected = await router.SelectAsync(
            new OwnerId("owner-a"),
            "use the probe request capability",
            CancellationToken.None);

        Assert.Equal(1, search.CallCount);
        Assert.Equal("use the probe request capability", search.LastPrompt);
        Assert.Single(selected);
    }

    [Fact(DisplayName = "inactive unknown and poisoned vector candidates never become model tools")]
    public async Task InactiveUnknownAndPoisonedCandidatesAreRejected()
    {
        var catalog = ActiveCapabilityCatalog.Create([ProbeModule()]);
        var search = new RecordingCandidateSearch(
        [
            CandidateFromProjectionShape(
                kind: CapabilityKinds.Synapse,
                contractId: "moduletests.probe-request",
                schemaVersion: 99,
                moduleId: null,
                neuronContractId: "moduletests.probe-neuron",
                sourceKey: "poisoned"),
            CandidateFromProjectionShape(
                kind: CapabilityKinds.Synapse,
                contractId: "unknown.synapse",
                schemaVersion: 1,
                moduleId: null,
                neuronContractId: null,
                sourceKey: "unknown"),
            CandidateFromProjectionShape(
                kind: CapabilityKinds.Neuron,
                contractId: "inactive.neuron",
                schemaVersion: null,
                moduleId: null,
                neuronContractId: "inactive.neuron",
                sourceKey: "inactive"),
            CandidateFromProjectionShape(
                kind: CapabilityKinds.Synapse,
                contractId: "moduletests.probe-request",
                schemaVersion: 1,
                moduleId: null,
                neuronContractId: "moduletests.probe-neuron",
                sourceKey: "good"),
        ]);
        var router = new CapabilityRouter(catalog, search);

        var selected = await router.SelectAsync(
            new OwnerId("owner-a"),
            NaturalLanguagePrompt,
            CancellationToken.None);

        Assert.Equal(1, search.CallCount);
        var capability = Assert.Single(selected);
        Assert.Equal("moduletests.probe-request", capability.ContractId);
        Assert.Equal(1, capability.SchemaVersion);
        Assert.Equal(
            ValidatedCapability.ToolNameFor("moduletests.probe-request", 1),
            capability.ToolName);
    }

    [Fact(DisplayName = "poison-only semantic hits never fall through into tools for NL prompts")]
    public async Task PoisonOnlySemanticHitsDoNotMaterializeTools()
    {
        var catalog = ActiveCapabilityCatalog.Create([ProbeModule()]);
        AssertNoExactTermOverlap(catalog, NaturalLanguagePrompt);

        var search = new RecordingCandidateSearch(
        [
            CandidateFromProjectionShape(
                kind: CapabilityKinds.Synapse,
                contractId: "moduletests.probe-request",
                schemaVersion: 99,
                moduleId: null,
                neuronContractId: "moduletests.probe-neuron",
                sourceKey: "stale-schema"),
            CandidateFromProjectionShape(
                kind: CapabilityKinds.Synapse,
                contractId: "forged.synapse",
                schemaVersion: 1,
                moduleId: null,
                neuronContractId: "moduletests.probe-neuron",
                sourceKey: "forged"),
        ]);
        var router = new CapabilityRouter(catalog, search);

        var selected = await router.SelectAsync(
            new OwnerId("owner-a"),
            NaturalLanguagePrompt,
            CancellationToken.None);

        Assert.Equal(1, search.CallCount);
        Assert.Empty(selected);
    }

    [Fact(DisplayName = "owner-inaccessible and emitted-only synapses are not offered as tools")]
    public void EmittedOnlySynapsesAreNotTools()
    {
        var catalog = ActiveCapabilityCatalog.Create([ProbeModule()]);
        var validator = new ExactCapabilityValidator(catalog);

        var selected = validator.Validate(
        [
            new CapabilityCandidate(
                CapabilityKinds.Synapse,
                "moduletests.probe-emitted",
                SchemaVersion: 1,
                ModuleId: null,
                NeuronContractId: "moduletests.probe-neuron",
                BehaviorId: null,
                ArtifactHash: null,
                SourceKey: "emitted"),
        ],
        limit: 8);

        Assert.Empty(selected);
    }

    [Fact(DisplayName = "stale schema versions from vector metadata never override exact catalog authority")]
    public void StaleSchemaVersionsNeverGrantAuthority()
    {
        var catalog = ActiveCapabilityCatalog.Create([ProbeModule()]);
        var validator = new ExactCapabilityValidator(catalog);

        Assert.True(catalog.TryGetSynapse("moduletests.probe-request", schemaVersion: 1, out _));
        Assert.False(catalog.TryGetSynapse("moduletests.probe-request", schemaVersion: 99, out _));

        var selected = validator.Validate(
        [
            new CapabilityCandidate(
                CapabilityKinds.Synapse,
                "moduletests.probe-request",
                SchemaVersion: 99,
                ModuleId: null,
                NeuronContractId: "moduletests.probe-neuron",
                BehaviorId: null,
                ArtifactHash: null,
                SourceKey: "stale"),
        ],
        limit: 8);

        Assert.Empty(selected);
    }

    private static void AssertNoExactTermOverlap(ActiveCapabilityCatalog catalog, string prompt)
    {
        var validator = new ExactCapabilityValidator(catalog);
        Assert.Empty(validator.ResolveExactTerms(prompt, limit: 8));
    }

    private static CapabilityCandidate CandidateFromProjectionShape(
        string kind,
        string contractId,
        int? schemaVersion,
        string? moduleId,
        string? neuronContractId,
        string sourceKey)
        => new(
            Kind: kind,
            ContractId: contractId,
            SchemaVersion: schemaVersion,
            ModuleId: moduleId,
            NeuronContractId: neuronContractId,
            BehaviorId: null,
            ArtifactHash: null,
            SourceKey: sourceKey);

    private static ScriptedModule ProbeModule()
    {
        var request = new SynapseCapabilityDescriptor(
            "moduletests.probe-request",
            schemaVersion: 1,
            "Probe request for automatic discovery",
            """{"type":"object","properties":{"text":{"type":"string"}}}""",
            ["probe request capability"]);
        var emitted = new SynapseCapabilityDescriptor(
            "moduletests.probe-emitted",
            schemaVersion: 1,
            "Probe emitted fact",
            """{"type":"object"}""",
            []);
        var neuron = new NeuronCapabilityDescriptor(
            "moduletests.probe-neuron",
            "Probe neuron",
            "probe",
            [request],
            [emitted]);
        return new ScriptedModule(
            new ModuleId("digitalbrain.testing.capability-probe"),
            new CapabilityManifest(
                new ModuleId("digitalbrain.testing.capability-probe"),
                "1.0.0",
                "Capability probe module for discovery",
                [],
                [neuron]));
    }

    private sealed class ScriptedCandidateSearch(IReadOnlyList<CapabilityCandidate> candidates) : ICapabilityCandidateSearch
    {
        public Task<IReadOnlyList<CapabilityCandidate>> SearchAsync(
            OwnerId owner,
            string prompt,
            int limit,
            CancellationToken cancellationToken)
            => Task.FromResult(candidates);
    }

    private sealed class RecordingCandidateSearch(IReadOnlyList<CapabilityCandidate> candidates) : ICapabilityCandidateSearch
    {
        public int CallCount { get; private set; }

        public OwnerId? LastOwner { get; private set; }

        public string? LastPrompt { get; private set; }

        public int LastLimit { get; private set; }

        public Task<IReadOnlyList<CapabilityCandidate>> SearchAsync(
            OwnerId owner,
            string prompt,
            int limit,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastOwner = owner;
            LastPrompt = prompt;
            LastLimit = limit;
            return Task.FromResult(candidates);
        }
    }
}
