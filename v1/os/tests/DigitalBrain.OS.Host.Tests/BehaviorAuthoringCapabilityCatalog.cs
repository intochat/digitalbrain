using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.HostTests;

public sealed class BehaviorAuthoringCapabilityCatalog
{
    private const string NeuronContractId = "ai.behavior-authoring";
    private const string ProposeContractId = "ai.propose-behavior-change-request";
    private const string ApproveContractId = "ai.approve-behavior-change";

    [Fact(DisplayName =
        "drafting a behavior change is an accepted capability of the behavior authoring neuron")]
    public void DraftingIsAnAcceptedCapability()
    {
        var catalog = ActiveCapabilityCatalog.Create([new AIModule()]);

        Assert.True(catalog.TryGetNeuron(NeuronContractId, out var neuron));
        Assert.NotNull(neuron);
        Assert.Contains(neuron.Accepted, synapse => synapse.ContractId == ProposeContractId);
    }

    [Fact(DisplayName =
        "a drafting request becomes a model tool whose schema hides the command identity")]
    public void DraftingBecomesAModelTool()
    {
        var catalog = ActiveCapabilityCatalog.Create(ProductModuleComposition.ProductModules());
        var validator = new ExactCapabilityValidator(catalog);

        var selected = validator.Validate(
            [
                new CapabilityCandidate(
                    CapabilityKinds.Synapse,
                    ProposeContractId,
                    SchemaVersion: 1,
                    ModuleId: AIModule.Id.Value,
                    NeuronContractId: NeuronContractId,
                    BehaviorId: null,
                    ArtifactHash: null,
                    SourceKey: $"{ProposeContractId}@v1"),
            ],
            limit: CapabilityRouter.DefaultLimit);

        var capability = Assert.Single(selected);
        Assert.Equal(ValidatedCapability.ToolNameFor(ProposeContractId, 1), capability.ToolName);
        Assert.DoesNotContain(
            "commandId",
            SynapseCapabilityTool.ModelSchemaFor(capability.JsonSchema),
            StringComparison.Ordinal);
    }

    [Fact(DisplayName =
        "approving a behavior change enters no catalog anywhere in the product, so no model tool can carry it")]
    public void ApprovalIsNotACapabilityAnywhere()
    {
        var catalog = ActiveCapabilityCatalog.Create(ProductModuleComposition.ProductModules());

        Assert.False(catalog.TryGetSynapse(ApproveContractId, 1, out _));
        foreach (var module in catalog.Modules)
        {
            foreach (var neuron in module.Neurons)
            {
                Assert.DoesNotContain(neuron.Accepted, synapse => synapse.ContractId == ApproveContractId);
                Assert.DoesNotContain(neuron.Emitted, synapse => synapse.ContractId == ApproveContractId);
            }
        }
    }

    [Fact(DisplayName =
        "approval is not a synapse at all: it reaches the brain only as a client entry point method")]
    public void ApprovalTravelsOnlyAsAClientEntryPointCall()
    {
        Assert.False(typeof(Synapse).IsAssignableFrom(typeof(ApproveBehaviorChange)));
        Assert.NotNull(typeof(IBehaviorAuthoring).GetCustomAttribute<ClientEntryPointAttribute>());
        Assert.NotNull(typeof(IBehaviorAuthoring).GetMethod(nameof(IBehaviorAuthoring.Approve)));
    }
}
