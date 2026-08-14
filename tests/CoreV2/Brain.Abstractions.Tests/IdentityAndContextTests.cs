using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Identity;
using Xunit;

namespace Brain.Abstractions.Tests;

public sealed class IdentityAndContextTests
{
    [Fact]
    public void Contract_id_requires_module_name_contract_name_and_major_version()
    {
        Assert.Throws<ArgumentException>(() => new ContractId("proof"));
        Assert.Throws<ArgumentException>(() => new ContractId("proof/run"));
        Assert.Throws<ArgumentException>(() => new ContractId("proof/run@0"));

        var id = new ContractId("proof/run@1");

        Assert.Equal("proof/run@1", id.Value);
    }

    [Fact]
    public void Activity_context_cannot_pair_an_empty_workspace_with_a_principal()
    {
        var principal = new PrincipalId("principal/alice");
        var activity = BrainActivityId.New();

        Assert.Throws<ArgumentException>(() =>
            new ActivityContext(WorkspaceId.Empty, principal, activity, new CorrelationId("corr/1")));
    }

    [Fact]
    public void Contexts_reject_default_identity_values()
    {
        var workspace = new WorkspaceId("workspace/acme");
        var principal = new PrincipalId("principal/alice");
        var correlation = new CorrelationId("corr/1");

        Assert.Throws<ArgumentException>(() =>
            new WorkspaceContext(workspace, default, isServicePrincipal: false));
        Assert.Throws<ArgumentException>(() =>
            new ActivityContext(workspace, principal, default, correlation));
        Assert.Throws<ArgumentException>(() =>
            new ActivityContext(workspace, principal, BrainActivityId.New(), default));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void String_backed_identifiers_reject_empty_values(string value)
    {
        Assert.Throws<ArgumentException>(() => new WorkspaceId(value));
        Assert.Throws<ArgumentException>(() => new PrincipalId(value));
        Assert.Throws<ArgumentException>(() => new ModuleId(value));
        Assert.Throws<ArgumentException>(() => new OperationId(value));
        Assert.Throws<ArgumentException>(() => new CapabilityId(value));
        Assert.Throws<ArgumentException>(() => new CapabilityUseName(value));
        Assert.Throws<ArgumentException>(() => new NeuronRoleId(value));
        Assert.Throws<ArgumentException>(() => new CorrelationId(value));
        Assert.Throws<ArgumentException>(() => new IdempotencyKey(value));
    }

    [Fact]
    public void Capability_use_name_is_a_nonempty_stable_identifier()
    {
        var name = new CapabilityUseName("classification/customer-42");

        Assert.Equal("classification/customer-42", name.Value);
    }

    [Fact]
    public void Guid_backed_identifiers_reject_empty_values_and_new_factories_create_values()
    {
        Assert.Throws<ArgumentException>(() => new BrainActivityId(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new SynapseKey(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new WiringId(Guid.Empty));

        Assert.NotEqual(Guid.Empty, BrainActivityId.New().Value);
        Assert.NotEqual(Guid.Empty, SynapseKey.New().Value);
        Assert.NotEqual(Guid.Empty, WiringId.New().Value);
    }

    [Fact]
    public void Delegation_intersect_only_retains_shared_operation_and_capability_grants()
    {
        var run = new OperationId("proof/run");
        var stop = new OperationId("proof/stop");
        var invoke = new CapabilityId("ai/invoke");
        var read = new CapabilityId("memory/read");
        var granted = new Delegation([run, stop], [invoke]);
        var requested = new Delegation([run], [invoke, read]);

        var intersection = granted.Intersect(requested);

        Assert.Equal([run], intersection.Operations);
        Assert.Equal([invoke], intersection.Capabilities);
    }
}
