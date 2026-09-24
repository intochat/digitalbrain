using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Identity;
using DigitalBrain.Identity.Grants;
using Xunit;

namespace DigitalBrain.Tests;

// A caller that claims a workspace it is not a member of is denied by the single call filter.
// This exercises the same pipeline the neurons run, with the grant stage in place.
public sealed class CrossWorkspaceFacts
{
    [Fact]
    public async Task ACallerCannotActInsideAWorkspaceItIsNotAMemberOf()
    {
        var member = new Member
        {
            PrincipalId = "owner",
            AccountId = "account-1",
            WorkspaceId = "workspace-b",
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
                WorkspaceId = "workspace-a",
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
        public ValueTask<Member?> FindMemberAsync(CallerContext caller, CancellationToken cancellationToken)
            => ValueTask.FromResult(member);

        public ValueTask<IReadOnlyList<Grant>> ListGrantsAsync(CallerContext caller, CancellationToken cancellationToken)
            => ValueTask.FromResult(grants);

        public ValueTask ConsumeOnceAsync(CallerContext caller, IReadOnlyList<Grant> consumed, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }
}
