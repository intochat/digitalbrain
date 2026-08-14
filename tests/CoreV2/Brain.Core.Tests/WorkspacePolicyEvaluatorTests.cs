using Brain.Abstractions.Context;
using Brain.Abstractions.Capabilities;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Operations;
using Brain.Abstractions.Policy;
using Brain.Core.Modules;
using Brain.Core.Policy;
using Xunit;

namespace Brain.Core.Tests;

public sealed class WorkspacePolicyEvaluatorTests
{
    [Fact]
    public void AuthorizeOperationAllowsAnInstalledOperationOwnedByItsEntryRole()
    {
        var operation = Operation("proof.run", "proof.entry", "proof");
        var evaluator = new WorkspacePolicyEvaluator(ModuleSetFor(operation));

        var decision = evaluator.AuthorizeOperation(Caller(), operation);

        Assert.Equal(PolicyDecision.Allowed, decision);
    }

    [Fact]
    public void AuthorizeOperationRefusesAnUninstalledOperation()
    {
        var installed = Operation("proof.run", "proof.entry", "proof");
        var attempted = Operation("proof.delete", "proof.entry", "proof");
        var evaluator = new WorkspacePolicyEvaluator(ModuleSetFor(installed));

        var decision = evaluator.AuthorizeOperation(Caller(), attempted);

        Assert.Equal(PolicyDecision.Refused, decision);
    }

    [Fact]
    public void AuthorizeCapabilityAllowsOnlyTheExactDescriptorPublishedByItsInstalledOwner()
    {
        var capability = new CapabilityDescriptor(
            new CapabilityId("proof/classify@1"),
            new ContractId("proof/classify-request@1"),
            new ContractId("proof/classify-result@1"),
            new ModuleId("proof"),
            new ContractVersion(1));
        var modules = ManifestValidator.Validate(
        [
            new ModuleManifest(
                new ModuleId("proof"),
                new ModuleVersion(1, 0, 0),
                [],
                [],
                [],
                [],
                [],
                [],
                [capability],
                []),
        ]);
        var evaluator = new WorkspacePolicyEvaluator(modules);
        var context = new ActivityContext(
            new WorkspaceId("workspace/sales"),
            new PrincipalId("principal/alice"),
            BrainActivityId.New(),
            new CorrelationId("correlation/capability"));
        var mismatched = new CapabilityDescriptor(
            capability.Id,
            capability.RequestContract,
            capability.ResultContract,
            capability.Owner,
            new ContractVersion(2));

        Assert.Equal(PolicyDecision.Allowed, evaluator.AuthorizeCapability(context, capability));
        Assert.Equal(PolicyDecision.Refused, evaluator.AuthorizeCapability(context, mismatched));
    }

    [Fact]
    public void PolicyDecisionExposesExactlyTheThreeDeclaredStates()
    {
        var states = Enum.GetValues<PolicyDecision>();

        Assert.Equal([PolicyDecision.Allowed, PolicyDecision.Refused, PolicyDecision.ConfirmationRequired], states);
    }

    [Fact]
    public void AuthorizeGraphChangeRefusesAnUninstalledTargetRole()
    {
        var operation = Operation("proof.run", "proof.entry", "proof");
        var evaluator = new WorkspacePolicyEvaluator(ModuleSetFor(operation));
        var context = new ActivityContext(
            new WorkspaceId("workspace/sales"),
            new PrincipalId("principal/alice"),
            BrainActivityId.New(),
            new CorrelationId("correlation/one"));
        var request = new GraphChangeRequest(
            GraphChangeKind.Install,
            new ModuleId("proof"),
            new ContractId("proof/finished@1"),
            new NeuronRoleId("missing.entry"));

        var decision = evaluator.AuthorizeGraphChange(context, request);

        Assert.Equal(PolicyDecision.Refused, decision);
    }

    [Fact]
    public void AuthorizeGraphChangeAllowsAnInstalledCrossModuleTargetRole()
    {
        var operation = Operation("proof.run", "proof.entry", "proof");
        var evaluator = new WorkspacePolicyEvaluator(ModuleSetForCrossModuleTarget(operation));
        var context = new ActivityContext(
            new WorkspaceId("workspace/sales"),
            new PrincipalId("principal/alice"),
            BrainActivityId.New(),
            new CorrelationId("correlation/one"));
        var request = new GraphChangeRequest(
            GraphChangeKind.Install,
            new ModuleId("proof"),
            new ContractId("proof/finished@1"),
            new NeuronRoleId("assessment.entry"));

        var decision = evaluator.AuthorizeGraphChange(context, request);

        Assert.Equal(PolicyDecision.Allowed, decision);
    }

    private static WorkspaceContext Caller()
        => new(new WorkspaceId("workspace/sales"), new PrincipalId("principal/alice"), isServicePrincipal: false);

    private static ModuleSet ModuleSetFor(OperationDescriptor operation)
        => ManifestValidator.Validate(
        [
            new ModuleManifest(
                new ModuleId("proof"),
                new ModuleVersion(1, 0, 0),
                [],
                [new NeuronRoleDescriptor(operation.EntryRole, NeuronScope.Workspace, operation.Owner)],
                [operation],
                [],
                [],
                [],
                [],
                []),
        ]);

    private static ModuleSet ModuleSetForCrossModuleTarget(OperationDescriptor operation)
        => ManifestValidator.Validate(
        [
            new ModuleManifest(
                new ModuleId("proof"),
                new ModuleVersion(1, 0, 0),
                [],
                [new NeuronRoleDescriptor(operation.EntryRole, NeuronScope.Workspace, operation.Owner)],
                [operation],
                [],
                [],
                [],
                [],
                []),
            new ModuleManifest(
                new ModuleId("assessment"),
                new ModuleVersion(1, 0, 0),
                [],
                [new NeuronRoleDescriptor(new NeuronRoleId("assessment.entry"), NeuronScope.Workspace, new ModuleId("assessment"))],
                [],
                [],
                [],
                [],
                [],
                []),
        ]);

    private static OperationDescriptor Operation(string id, string entryRole, string owner)
        => new(
            new OperationId(id),
            new ContractId($"{owner}/run-input@1"),
            new ContractId($"{owner}/run-result@1"),
            new NeuronRoleId(entryRole),
            new ModuleId(owner),
            new ContractVersion(1));
}
