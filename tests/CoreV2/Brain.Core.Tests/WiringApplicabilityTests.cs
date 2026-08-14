using Brain.Abstractions.Capabilities;
using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Events;
using Brain.Abstractions.Graph;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Operations;
using Brain.Abstractions.Wiring;
using Brain.Core.Modules;
using Brain.Core.Wiring;
using Xunit;

namespace Brain.Core.Tests;

public sealed class WiringApplicabilityTests
{
    [Fact]
    public void ApplicabilityUsesOnlyDeclarativeFrameworkFacts()
    {
        var fixture = new WiringFixture();

        var applicability = fixture.Evaluator.Evaluate(fixture.Version, fixture.Caller, fixture.Modules);

        Assert.Equal(WiringReadiness.Ready, applicability.Readiness);
        Assert.DoesNotContain("prompt", applicability.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("entity", applicability.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingRequiredCapabilityNeedsSetupWithoutInspectingRuntimeData()
    {
        var fixture = new WiringFixture(includeCapability: false);

        var applicability = fixture.Evaluator.Evaluate(fixture.Version, fixture.Caller, fixture.Modules);

        Assert.Equal(WiringReadiness.NeedsSetup, applicability.Readiness);
    }

    [Fact]
    public void PrincipalPolicyPrerequisiteNeedsAuthorization()
    {
        var fixture = new WiringFixture(prerequisites: [new WiringPolicyPrerequisite("delegated-access", WiringPrerequisiteKind.PrincipalAuthorization)]);

        var applicability = fixture.Evaluator.Evaluate(fixture.Version, fixture.Caller, fixture.Modules);

        Assert.Equal(WiringReadiness.NeedsAuthorization, applicability.Readiness);
    }

    [Fact]
    public void WiringContractHasNoRuntimeOrProviderDataSurface()
    {
        var forbidden = new[] { "endpoint", "synapse", "graph", "prompt", "entity", "payload", "transcript", "token", "usage", "predicate", "code" };
        var properties = typeof(WiringVersion).GetProperties().Select(property => property.Name);

        Assert.DoesNotContain(properties, property => forbidden.Any(term => property.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class WiringFixture
    {
        public WiringFixture(bool includeCapability = true, IReadOnlyCollection<WiringPolicyPrerequisite>? prerequisites = null)
        {
            Caller = new WorkspaceContext(new WorkspaceId("workspace/one"), new PrincipalId("principal/alice"), isServicePrincipal: false);
            Operation = new OperationDescriptor(
                new OperationId("proof/operate@1"),
                new ContractId("proof/requested@1"),
                new ContractId("proof/result@1"),
                new NeuronRoleId("proof.entry"),
                new ModuleId("proof"),
                new ContractVersion(1));
            Capability = new CapabilityDescriptor(
                new CapabilityId("proof/lookup@1"),
                new ContractId("proof/lookup-requested@1"),
                new ContractId("proof/lookup-result@1"),
                new ModuleId("capability"),
                new ContractVersion(1));
            Version = new WiringVersion(
                WiringId.New(),
                1,
                null,
                BrainActivityId.New(),
                Operation.Id,
                Operation.Version,
                [new WiringRoute(new NeuronRoleId("proof.source"), new NeuronRoleId("proof.target"), new ContractId("proof/produced@1"), new WiringSlotId("proof-route"), null)],
                [Capability.Id],
                prerequisites ?? []);
            Modules = ManifestValidator.Validate(
            [
                new ModuleManifest(new ModuleId("proof"), new ModuleVersion(1, 0, 0), [],
                    [new NeuronRoleDescriptor(Operation.EntryRole, NeuronScope.Workspace, new ModuleId("proof")), new NeuronRoleDescriptor(new NeuronRoleId("proof.source"), NeuronScope.Workspace, new ModuleId("proof")), new NeuronRoleDescriptor(new NeuronRoleId("proof.target"), NeuronScope.Workspace, new ModuleId("proof"))],
                    [Operation],
                    [new EventDescriptor(new ContractId("proof/produced@1"), new ModuleId("proof"), typeof(Produced), EventVisibility.Published)],
                    [new ContractId("proof/produced@1")], [], [], []),
                new ModuleManifest(new ModuleId("capability"), new ModuleVersion(1, 0, 0), [], [], [], [], [], [], includeCapability ? [Capability] : [], []),
            ]);
            Evaluator = new WiringApplicabilityEvaluator();
        }

        public WorkspaceContext Caller { get; }
        public OperationDescriptor Operation { get; }
        public CapabilityDescriptor Capability { get; }
        public WiringVersion Version { get; }
        public ModuleSet Modules { get; }
        public WiringApplicabilityEvaluator Evaluator { get; }
    }

    private sealed class Produced : IDomainEvent;
}
