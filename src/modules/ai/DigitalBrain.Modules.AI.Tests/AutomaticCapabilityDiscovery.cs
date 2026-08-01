using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class AutomaticCapabilityDiscovery
{
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

    [Fact(DisplayName = "inactive unknown and poisoned vector candidates never become model tools")]
    public async Task InactiveUnknownAndPoisonedCandidatesAreRejected()
    {
        var catalog = ActiveCapabilityCatalog.Create([ProbeModule()]);
        var search = new ScriptedCandidateSearch(
        [
            new CapabilityCandidate(
                CapabilityKinds.Synapse,
                "moduletests.probe-request",
                SchemaVersion: 99,
                ModuleId: null,
                NeuronContractId: "moduletests.probe-neuron",
                BehaviorId: null,
                ArtifactHash: null,
                SourceKey: "poisoned"),
            new CapabilityCandidate(
                CapabilityKinds.Synapse,
                "unknown.synapse",
                SchemaVersion: 1,
                ModuleId: null,
                NeuronContractId: null,
                BehaviorId: null,
                ArtifactHash: null,
                SourceKey: "unknown"),
            new CapabilityCandidate(
                CapabilityKinds.Neuron,
                "inactive.neuron",
                SchemaVersion: null,
                ModuleId: null,
                NeuronContractId: "inactive.neuron",
                BehaviorId: null,
                ArtifactHash: null,
                SourceKey: "inactive"),
            new CapabilityCandidate(
                CapabilityKinds.Synapse,
                "moduletests.probe-request",
                SchemaVersion: 1,
                ModuleId: null,
                NeuronContractId: "moduletests.probe-neuron",
                BehaviorId: null,
                ArtifactHash: null,
                SourceKey: "good"),
        ]);
        var router = new CapabilityRouter(catalog, search);

        var selected = await router.SelectAsync(
            new OwnerId("owner-a"),
            "probe",
            CancellationToken.None);

        var capability = Assert.Single(selected);
        Assert.Equal("moduletests.probe-request", capability.ContractId);
        Assert.Equal(1, capability.SchemaVersion);
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

    private sealed class ScriptedModule(ModuleId id, CapabilityManifest capabilities) : ICompiledModule
    {
        public ModuleId Id { get; } = id;

        public CapabilityManifest Capabilities { get; } = capabilities;

        public void PrepareSerialization(IServiceCollection services)
        {
        }

        public void Activate(ISiloBuilder builder)
        {
        }
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
}
