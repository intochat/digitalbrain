using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Contracts.Identity;
using DigitalBrain.Platform.Identity.Grants;
using Xunit;

namespace DigitalBrain.Platform.Tests.Unit.Identity;

// A caller that claims a workspace it is not a member of is denied by the single call filter.
// This exercises the same pipeline the neurons run, with the grant stage in place.
public sealed class BrainMembershipFilterFacts
{
    [Fact]
    public async Task ACallerCannotActInsideAWorkspaceItIsNotAMemberOf()
    {
        var member = new Member
        {
            PrincipalId = "owner",
            AccountId = "account-1",
            BrainId = "workspace-b",
            Role = MemberRole.Owner,
            DisplayName = "Owner",
            JoinedAt = DateTimeOffset.UtcNow,
        };
        var filter = new CallFilter([new GrantCallFilterStage(new FakePolicy(member, []))]);
        var request = new CallRequest
        {
            Caller = new CallerContext
            {
                PrincipalId = "owner",
                AccountId = "account-1",
                BrainId = "workspace-a",
                Kind = CallerKind.User,
                StampedBy = TrustedEdge.AuthenticatedHttp,
            },
            TargetNeuron = "workspace-a-neuron",
            Operation = "Read",
        };

        var decision = await filter.AuthorizeAsync(request, TestContext.Current.CancellationToken);
        Assert.False(decision.Allowed);
        Assert.Equal(CallDenial.OutsideWorkspace, decision.Denial);
    }

    private sealed class FakePolicy(Member? member, IReadOnlyList<Grant> grants) : IGrantPolicySource
    {
        public ValueTask<CallDecision?> AuthorizeAsync(CallRequest request, CancellationToken cancellationToken)
            => ValueTask.FromResult(GrantRules.Evaluate(request, member, grants));

        public ValueTask<IReadOnlyList<Grant>> ListGrantsAsync(CallerContext caller, CancellationToken cancellationToken)
            => ValueTask.FromResult(grants);

    }
}
