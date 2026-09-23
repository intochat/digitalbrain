using DigitalBrain.Identity;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class AccountFacts
{
    [Fact]
    public async Task OwnerIsProvisionedOnceAndMembersJoinByInvitationCode()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<IdentityModule>().StartAsync(ct);
        var directory = brain.Get<IIdentityDirectory>(IdentityGrains.Directory);

        var owner = await directory.EnsureOwnerAsync("owner", "default", "Owner", ct);
        Assert.Equal(MemberRole.Owner, owner.Role);
        Assert.Equal("default", owner.WorkspaceId);
        Assert.Equal(owner, await directory.EnsureOwnerAsync("owner", "default", "Owner", ct));

        var invitation = await directory.InviteAsync("default", "member@example.com", MemberRole.Member, ct);
        var member = await directory.AcceptInvitationAsync(invitation.Code, "member-1", "Member", ct);
        Assert.Equal(MemberRole.Member, member.Role);
        Assert.Equal(owner.AccountId, member.AccountId);

        Assert.Equal("default", (await directory.FindMemberAsync("member-1", ct))!.WorkspaceId);
        Assert.Equal(2, (await directory.ListMembersAsync(owner.AccountId, ct)).Count);

        var shared = await directory.ShareWorkspaceAsync(owner.AccountId, "second", "member-1", "Member", MemberRole.Member, ct);
        Assert.Equal("second", shared.WorkspaceId);
    }

    [Fact]
    public async Task CanAccessAnswersOnlyForOwnedOrSharedWorkspaces()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<IdentityModule>().StartAsync(ct);
        var directory = brain.Get<IIdentityDirectory>(IdentityGrains.Directory);
        var owner = await directory.EnsureOwnerAsync("owner", "default", "Owner", ct);

        Assert.True(await directory.CanAccessAsync("owner", "default", ct));
        Assert.False(await directory.CanAccessAsync("owner", "elsewhere", ct));
        Assert.False(await directory.CanAccessAsync("stranger", "default", ct));

        var invitation = await directory.InviteAsync("default", null, MemberRole.Member, ct);
        await directory.AcceptInvitationAsync(invitation.Code, "member-1", "Member", ct);
        Assert.True(await directory.CanAccessAsync("member-1", "default", ct));
        Assert.False(await directory.CanAccessAsync("member-1", "shared", ct));

        await directory.ShareWorkspaceAsync(owner.AccountId, "shared", "member-1", "Member", MemberRole.Member, ct);
        Assert.True(await directory.CanAccessAsync("member-1", "shared", ct));
    }

    [Fact]
    public async Task AnInvitationCodeIsSingleUse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<IdentityModule>().StartAsync(ct);
        var directory = brain.Get<IIdentityDirectory>(IdentityGrains.Directory);
        await directory.EnsureOwnerAsync("owner", "default", "Owner", ct);
        var invitation = await directory.InviteAsync("default", null, MemberRole.Member, ct);
        await directory.AcceptInvitationAsync(invitation.Code, "member-1", "Member", ct);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => directory.AcceptInvitationAsync(invitation.Code, "member-2", "Member", ct));
    }
}
