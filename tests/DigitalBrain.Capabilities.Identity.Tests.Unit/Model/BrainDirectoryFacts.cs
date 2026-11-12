namespace DigitalBrain.Capabilities.Identity.Tests.Unit.Model;

using DigitalBrain.Capabilities.Identity.Model;
using Xunit;

public sealed class BrainDirectoryFacts
{
    [Fact]
    public void OwnerCheckRequiresDirectHumanCallerMatchingOwnerPrincipal()
    {
        var owner = new Member { PrincipalId = "alice", AccountId = "acc-1", BrainId = "b1", Role = MemberRole.Owner, DisplayName = "Alice" };
        var dir = new BrainDirectory { BrainId = "b1", AccountId = "acc-1", Members = [owner] };

        Assert.True(dir.OwnedBy(new CapabilityCaller("alice", "acc-1", "b1")));
        Assert.False(dir.OwnedBy(new CapabilityCaller("bob", "acc-2", "b1")));
        Assert.False(dir.OwnedBy(new CapabilityCaller("app:test", "acc-1", "b1", "test-app")));
    }

    [Fact]
    public void AddMemberByOwnerSucceedsAndEmitsMemberAdded()
    {
        var owner = new Member { PrincipalId = "alice", AccountId = "acc-1", BrainId = "b1", Role = MemberRole.Owner, DisplayName = "Alice" };
        var dir = new BrainDirectory { BrainId = "b1", AccountId = "acc-1", Members = [owner] };
        var now = DateTimeOffset.UtcNow;

        var guest = new Member { PrincipalId = "bob", AccountId = "acc-2", BrainId = "b1", Role = MemberRole.Member, DisplayName = "Bob" };
        var outcome = dir.AddMember(new CapabilityCaller("alice", "acc-1", "b1"), guest, now);

        Assert.Equal(Refusal.None, outcome.Refused);
        Assert.True(outcome.Changed);
        Assert.Contains(outcome.Next.Members, m => m.PrincipalId == "bob");
        Assert.Single(outcome.Facts);
        var signal = Assert.IsType<MemberAdded>(outcome.Facts[0]);
        Assert.Equal("bob", signal.Member.PrincipalId);
        Assert.Equal("b1", signal.Member.BrainId);
    }

    [Fact]
    public void AddMemberByNonOwnerRefuses()
    {
        var owner = new Member { PrincipalId = "alice", AccountId = "acc-1", BrainId = "b1", Role = MemberRole.Owner, DisplayName = "Alice" };
        var guest = new Member { PrincipalId = "bob", AccountId = "acc-2", BrainId = "b1", Role = MemberRole.Member, DisplayName = "Bob" };
        var dir = new BrainDirectory { BrainId = "b1", AccountId = "acc-1", Members = [owner, guest] };
        var now = DateTimeOffset.UtcNow;

        var third = new Member { PrincipalId = "charlie", AccountId = "acc-3", BrainId = "b1", Role = MemberRole.Member, DisplayName = "Charlie" };
        var outcome = dir.AddMember(new CapabilityCaller("bob", "acc-2", "b1"), third, now);

        Assert.Equal(Refusal.NotOwner, outcome.Refused);
        Assert.False(outcome.Changed);
        Assert.Throws<UnauthorizedAccessException>(() => outcome.OrThrow(new CapabilityCaller("bob", "acc-2", "b1"), "b1"));
    }

    [Fact]
    public void RemoveMemberByNonOwnerRefuses()
    {
        var owner = new Member { PrincipalId = "alice", AccountId = "acc-1", BrainId = "b1", Role = MemberRole.Owner, DisplayName = "Alice" };
        var guest = new Member { PrincipalId = "bob", AccountId = "acc-2", BrainId = "b1", Role = MemberRole.Member, DisplayName = "Bob" };
        var dir = new BrainDirectory { BrainId = "b1", AccountId = "acc-1", Members = [owner, guest] };

        var outcome = dir.RemoveMember(new CapabilityCaller("bob", "acc-2", "b1"), "alice");
        Assert.Equal(Refusal.NotOwner, outcome.Refused);
        Assert.Throws<UnauthorizedAccessException>(() => outcome.OrThrow(new CapabilityCaller("bob", "acc-2", "b1"), "b1"));
    }

    [Fact]
    public void OnceGrantIsConsumedOnAuthorize()
    {
        var owner = new Member { PrincipalId = "alice", AccountId = "acc-1", BrainId = "b1", Role = MemberRole.Owner, DisplayName = "Alice" };
        var grant = new Grant { AppId = "app-1", WorkspaceId = "b1", SemanticTypeId = "notes", Mode = GrantMode.Once };
        var dir = new BrainDirectory { BrainId = "b1", AccountId = "acc-1", Members = [owner], Grants = [grant] };

        var caller = new CapabilityCaller("app:app-1", "acc-1", "b1", "app-1");
        var request = new CallRequest { TargetNeuron = "notes", Operation = "read", SemanticTypeIds = ["notes"] };

        var (outcome, decision) = dir.Authorize(caller, request);
        Assert.True(decision.Allowed);
        Assert.True(decision.ConsumesOnce);
        Assert.Equal(Refusal.None, outcome.Refused);
        Assert.True(outcome.Changed);
        Assert.True(outcome.Next.Grants[0].Revoked);
        Assert.Single(outcome.Facts);
        var signal = Assert.IsType<GrantConsumed>(outcome.Facts[0]);
        Assert.Equal("app-1", signal.AppId);
        Assert.Equal("notes", signal.SemanticTypeId);

        // Subsequent authorize on updated state denies
        var (_, secondDecision) = outcome.Next.Authorize(caller, request);
        Assert.False(secondDecision.Allowed);
    }
}
