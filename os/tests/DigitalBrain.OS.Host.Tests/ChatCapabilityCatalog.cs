using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.HostTests;

public sealed class ChatCapabilityCatalog
{
    private const string NeuronContractId = "chat";
    private const string ReadTranscriptContractId = "chat.read-transcript-request";

    [Fact(DisplayName =
        "the chat read-transcript request synapse enters the active capability catalog as an accepted capability of the chat neuron")]
    public void ReadTranscriptRequestIsAnAcceptedCapability()
    {
        var catalog = ActiveCapabilityCatalog.Create([new ChatModule()]);

        Assert.True(catalog.TryGetNeuron(NeuronContractId, out var neuron));
        Assert.NotNull(neuron);
        Assert.Contains(ReadTranscriptContractId, neuron.Accepted.Select(static synapse => synapse.ContractId));
        Assert.Contains(
            neuron.Accepted,
            synapse => synapse.ContractId == ReadTranscriptContractId && synapse.SchemaVersion == 1);
    }

    [Fact(DisplayName =
        "the read-transcript request binds to a CLR type and to the chat grain, so the tool seam can materialize it")]
    public void ReadTranscriptRequestResolvesToRuntimeTypes()
    {
        ICompiledModule[] modules = [new ChatModule()];
        var catalog = ActiveCapabilityCatalog.Create(modules);
        var typeMap = ActiveModuleContractTypeMap.Create(modules, catalog);

        Assert.True(typeMap.TryGetSynapseType(ReadTranscriptContractId, 1, out var request));
        Assert.Equal(typeof(ReadTranscriptRequest), request);

        Assert.True(typeMap.TryGetNeuronGrainType(NeuronContractId, out var grainType));
        Assert.Equal("chat", grainType);
    }

    [Fact(DisplayName =
        "a discovered read-transcript candidate validates against the whole product catalog and becomes a model tool")]
    public void DiscoveredReadTranscriptCandidateBecomesAModelTool()
    {
        var catalog = ActiveCapabilityCatalog.Create(ProductModuleComposition.ProductModules());
        var validator = new ExactCapabilityValidator(catalog);

        var selected = validator.Validate(
            [
                new CapabilityCandidate(
                    CapabilityKinds.Synapse,
                    ReadTranscriptContractId,
                    SchemaVersion: 1,
                    ModuleId: ChatModule.Id.Value,
                    NeuronContractId: NeuronContractId,
                    BehaviorId: null,
                    ArtifactHash: null,
                    SourceKey: $"{ReadTranscriptContractId}@v1"),
            ],
            limit: CapabilityRouter.DefaultLimit);

        var capability = Assert.Single(selected);
        Assert.Equal(ReadTranscriptContractId, capability.ContractId);
        Assert.Equal(NeuronContractId, capability.NeuronContractId);
        Assert.Equal(ValidatedCapability.ToolNameFor(ReadTranscriptContractId, 1), capability.ToolName);
        Assert.Equal(ChatModule.Id.Value, capability.ModuleId);
        Assert.Contains("durable transcript kept", capability.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "commandId",
            SynapseCapabilityTool.ModelSchemaFor(capability.JsonSchema),
            StringComparison.Ordinal);
    }

    [Theory(DisplayName =
        "a mundane prompt offers no read-transcript tool through the exact-term fallback")]
    [InlineData("hello there")]
    [InlineData("what is the weather like today")]
    [InlineData("compare Gemma and Llama on this")]
    [InlineData("thanks, that is all for now")]
    [InlineData("summarise the document I gave you")]
    [InlineData("what does this function return")]
    [InlineData("open a bank account for me")]
    [InlineData("which country has the largest population")]
    [InlineData("is there a discount available")]
    [InlineData("increment the counter")]
    [InlineData("turn on the lights")]
    [InlineData("how do I turn this off")]
    [InlineData("recording starts at noon")]
    public async Task MundanePromptsOfferNoReadTranscriptTool(string prompt)
    {
        var catalog = ActiveCapabilityCatalog.Create(ProductModuleComposition.ProductModules());
        var router = new CapabilityRouter(catalog);

        var selected = await router.SelectAsync(
            new Abstractions.OwnerId("owner-a"),
            prompt,
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain(selected, capability => capability.ContractId == ReadTranscriptContractId);
    }

    [Fact(DisplayName =
        "an owner asking about a conversation's transcript still reaches the read-transcript tool through the exact-term fallback")]
    public async Task ATranscriptQuestionStillReachesTheReadTranscriptTool()
    {
        var catalog = ActiveCapabilityCatalog.Create([new ChatModule()]);
        var router = new CapabilityRouter(catalog);

        var selected = await router.SelectAsync(
            new Abstractions.OwnerId("owner-a"),
            "can you show me the transcript of another conversation?",
            TestContext.Current.CancellationToken);

        Assert.Contains(selected, capability => capability.ContractId == ReadTranscriptContractId);
    }
}
