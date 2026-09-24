using DigitalBrain.Identity;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class AccountFacts
{
    [Fact]
    public async Task CreatingAWorkspaceUsesAuthenticatedOwnershipAndDeniesOutsiders()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<IdentityModule>().StartAsync(ct);
        var directory = brain.Get<IIdentityDirectory>(IdentityGrains.Directory);
        var owner = await directory.RegisterAsync("alice", "correct-password", "Alice", ct);
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        var anonymous = await IdentityEndpoints.CreateWorkspaceAsync(http, brain, ct);
        Assert.Equal(401, ((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)anonymous).StatusCode);
        http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, owner.PrincipalId),
            new System.Security.Claims.Claim("intochat.account", owner.AccountId),
        ], "test"));
        var result = await IdentityEndpoints.CreateWorkspaceAsync(http, brain, ct);
        var workspace = Assert.IsType<Member>(((Microsoft.AspNetCore.Http.IValueHttpResult)result).Value);
        Assert.Equal(owner.AccountId, workspace.AccountId);
        Assert.NotEqual(owner.WorkspaceId, workspace.WorkspaceId);
        Assert.True(await directory.CanAccessAsync("alice", workspace.WorkspaceId, ct));
        Assert.False(await directory.CanAccessAsync("bob", workspace.WorkspaceId, ct));
        http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "bob"),
            new System.Security.Claims.Claim("intochat.account", owner.AccountId),
        ], "test"));
        var outsider = await IdentityEndpoints.CreateWorkspaceAsync(http, brain, ct);
        Assert.Equal(403, ((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)outsider).StatusCode);
    }

    [Fact]
    public async Task PasswordAuthenticationSeparatesAccountsAndRejectsImpersonation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<IdentityModule>().StartAsync(ct);
        var directory = brain.Get<IIdentityDirectory>(IdentityGrains.Directory);
        var first = await directory.RegisterAsync("alice", "correct-password", "Alice", ct);
        var second = await directory.RegisterAsync("bob", "another-password", "Bob", ct);
        Assert.NotEqual(first.AccountId, second.AccountId);
        Assert.NotEqual(first.WorkspaceId, second.WorkspaceId);
        Assert.Null(await directory.AuthenticateAsync("alice", "wrong-password", ct));
        Assert.Null(await directory.AuthenticateAsync("alice", "", ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => directory.RegisterAsync("owner", "correct-password", "Owner", ct));
        await Assert.ThrowsAsync<ArgumentException>(() => directory.RegisterAsync("weak", "short", "Weak", ct));
        Assert.Null(await directory.AuthenticateAsync("unknown", "correct-password", ct));
        Assert.Equal(first, await directory.AuthenticateAsync("alice", "correct-password", ct));
        Assert.False(await directory.CanAccessAsync("bob", first.WorkspaceId, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => directory.RegisterAsync("alice", "new-password", "Pretender", ct));
    }

    [Fact]
    public async Task OwnersHaveIndependentAccounts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<IdentityModule>().StartAsync(ct);
        var directory = brain.Get<IIdentityDirectory>(IdentityGrains.Directory);
        var first = await directory.EnsureOwnerAsync("alice", "alice-home", "Alice", ct);
        var second = await directory.EnsureOwnerAsync("bob", "bob-home", "Bob", ct);
        Assert.NotEqual(first.AccountId, second.AccountId);
    }


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
