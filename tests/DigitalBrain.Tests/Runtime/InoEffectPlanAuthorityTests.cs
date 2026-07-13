using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Runtime;

namespace DigitalBrain.Tests.Runtime;

public sealed class InoEffectPlanAuthorityTests
{
    private const string PlanId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string ActorScope = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
    private const string ToolId = "gmail.send";
    private const string SafeSummary = "send one approved acceptance email";

    [Fact]
    public void Issued_scope_is_bound_to_the_plan_actor_and_tool()
    {
        var authority = new InoEffectPlanAuthority(KeyRing());

        var scope = authority.Issue(PlanId, ActorScope, ToolId, SafeSummary);

        Assert.True(authority.TryValidate(scope, ActorScope, ToolId, SafeSummary, out var parsedPlanId));
        Assert.Equal(PlanId, parsedPlanId);
        Assert.False(authority.TryValidate(scope, new string('1', 64), ToolId, SafeSummary, out _));
        Assert.False(authority.TryValidate(scope, ActorScope, "salesforce.record.update", SafeSummary, out _));
        Assert.False(authority.TryValidate(scope, ActorScope, ToolId, "send a different email", out _));
    }

    [Fact]
    public void Forged_scope_is_rejected()
    {
        var authority = new InoEffectPlanAuthority(KeyRing());
        var scope = authority.Issue(PlanId, ActorScope, ToolId, SafeSummary);
        var replacement = scope[^1] == 'A' ? 'B' : 'A';
        var forged = scope[..^1] + replacement;

        Assert.False(authority.TryValidate(forged, ActorScope, ToolId, SafeSummary, out _));
        Assert.False(authority.TryValidate("plan.invalid", ActorScope, ToolId, SafeSummary, out _));
    }

    [Fact]
    public void Execution_proof_is_bound_to_the_approved_effect_and_idempotency_key()
    {
        var authority = new InoEffectPlanAuthority(KeyRing());
        var proof = authority.IssueExecutionProof(
            PlanId, ActorScope, "operation-1", ToolId, "effect-1", "provider-key-1");

        Assert.True(authority.ValidateExecutionProof(
            proof, PlanId, ActorScope, "operation-1", ToolId, "effect-1", "provider-key-1"));
        Assert.False(authority.ValidateExecutionProof(
            proof, PlanId, ActorScope, "operation-1", ToolId, "effect-2", "provider-key-1"));
        Assert.False(authority.ValidateExecutionProof(
            proof, PlanId, ActorScope, "operation-1", ToolId, "effect-1", "provider-key-2"));
    }

    private static RuntimeStateKeyRing KeyRing() => new(
        1,
        new Dictionary<int, byte[]> { [1] = Enumerable.Repeat((byte)0x11, 32).ToArray() },
        Enumerable.Repeat((byte)0x22, 32).ToArray());
}
