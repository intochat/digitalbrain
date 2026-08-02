using DigitalBrain.AI;
using DigitalBrain.Introspection;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.HostTests;

public sealed class IntrospectionCapabilityCatalog
{
    private const string NeuronContractId = "introspection";
    private const string TallyContractId = "introspection.tally-journal-request";
    private const string ReadJournalContractId = "introspection.read-journal-request";
    private const string ReadTopologyContractId = "introspection.read-topology-request";

    [Fact(DisplayName =
        "the introspection request synapses enter the active capability catalog as accepted capabilities of the introspection neuron")]
    public void IntrospectionRequestsAreAcceptedCapabilities()
    {
        var catalog = ActiveCapabilityCatalog.Create([new IntrospectionModule()]);

        Assert.True(catalog.TryGetNeuron(NeuronContractId, out var neuron));
        Assert.NotNull(neuron);
        Assert.Equal(
            [ReadJournalContractId, ReadTopologyContractId, TallyContractId],
            neuron.Accepted.Select(static synapse => synapse.ContractId).Order(StringComparer.Ordinal));
        Assert.All(neuron.Accepted, synapse => Assert.Equal(1, synapse.SchemaVersion));
    }

    [Fact(DisplayName =
        "the introspection requests bind to CLR types and to the introspection grain, so the tool seam can materialize them")]
    public void IntrospectionRequestsResolveToRuntimeTypes()
    {
        ICompiledModule[] modules = [new IntrospectionModule()];
        var catalog = ActiveCapabilityCatalog.Create(modules);
        var typeMap = ActiveModuleContractTypeMap.Create(modules, catalog);

        Assert.True(typeMap.TryGetSynapseType(TallyContractId, 1, out var tally));
        Assert.Equal(typeof(TallyJournalRequest), tally);
        Assert.True(typeMap.TryGetSynapseType(ReadJournalContractId, 1, out var read));
        Assert.Equal(typeof(ReadJournalRequest), read);
        Assert.True(typeMap.TryGetSynapseType(ReadTopologyContractId, 1, out var topology));
        Assert.Equal(typeof(ReadTopologyRequest), topology);

        Assert.True(typeMap.TryGetNeuronGrainType(NeuronContractId, out var grainType));
        Assert.Equal("introspection", grainType);
    }

    [Fact(DisplayName =
        "a discovered introspection candidate validates against the whole product catalog and becomes a model tool")]
    public void DiscoveredIntrospectionCandidateBecomesAModelTool()
    {
        var catalog = ActiveCapabilityCatalog.Create(ProductModuleComposition.ProductModules());
        var validator = new ExactCapabilityValidator(catalog);

        var selected = validator.Validate(
            [
                new CapabilityCandidate(
                    CapabilityKinds.Synapse,
                    TallyContractId,
                    SchemaVersion: 1,
                    ModuleId: IntrospectionModule.Id.Value,
                    NeuronContractId: NeuronContractId,
                    BehaviorId: null,
                    ArtifactHash: null,
                    SourceKey: $"{TallyContractId}@v1"),
            ],
            limit: CapabilityRouter.DefaultLimit);

        var capability = Assert.Single(selected);
        Assert.Equal(TallyContractId, capability.ContractId);
        Assert.Equal(NeuronContractId, capability.NeuronContractId);
        Assert.Equal(ValidatedCapability.ToolNameFor(TallyContractId, 1), capability.ToolName);
        Assert.Equal(IntrospectionModule.Id.Value, capability.ModuleId);
        Assert.Contains("Counts journaled synapses", capability.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "commandId",
            SynapseCapabilityTool.ModelSchemaFor(capability.JsonSchema),
            StringComparison.Ordinal);
    }

    [Theory(DisplayName =
        "a mundane prompt offers no introspection tool through the exact-term fallback")]
    [InlineData("hello there")]
    [InlineData("what is the weather like today")]
    [InlineData("compare Gemma and Llama on this")]
    [InlineData("thanks, that is all for now")]
    [InlineData("summarise the document I gave you")]
    public async Task MundanePromptsOfferNoIntrospectionTool(string prompt)
    {
        var catalog = ActiveCapabilityCatalog.Create(ProductModuleComposition.ProductModules());
        var router = new CapabilityRouter(catalog);

        var selected = await router.SelectAsync(
            new Abstractions.OwnerId("owner-a"),
            prompt,
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain(
            selected,
            capability => capability.ContractId.StartsWith("introspection.", StringComparison.Ordinal));
    }

    [Fact(DisplayName =
        "an owner asking about journalled volume still reaches the tally tool through the exact-term fallback")]
    public async Task AJournalQuestionStillReachesTheTallyTool()
    {
        var catalog = ActiveCapabilityCatalog.Create([new IntrospectionModule()]);
        var router = new CapabilityRouter(catalog);

        var selected = await router.SelectAsync(
            new Abstractions.OwnerId("owner-a"),
            "how often has a conversation recorded owner messages?",
            TestContext.Current.CancellationToken);

        Assert.Contains(selected, capability => capability.ContractId == TallyContractId);
    }
}
