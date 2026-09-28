using DigitalBrain.Identity;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Identity.Tests.Unit;

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
    public async Task CanAccessAnswersOnlyForOwnedOrSharedWorkspaces()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<IdentityModule>().StartAsync(ct);
        var directory = brain.Get<IIdentityDirectory>(IdentityGrains.Directory);
        var owner = await directory.RegisterAsync("alice", "correct-password", "Alice", ct);
        await directory.RegisterAsync("bob", "another-password", "Bob", ct);

        Assert.True(await directory.CanAccessAsync("alice", owner.WorkspaceId, ct));
        Assert.False(await directory.CanAccessAsync("alice", "elsewhere", ct));
        Assert.False(await directory.CanAccessAsync("bob", owner.WorkspaceId, ct));

        await directory.ShareWorkspaceAsync(owner.AccountId, owner.WorkspaceId, "bob", "Bob", MemberRole.Member, ct);
        Assert.True(await directory.CanAccessAsync("bob", owner.WorkspaceId, ct));
    }
}
