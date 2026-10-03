using DigitalBrain.Platform.Contracts.Identity;
using DigitalBrain.Platform.Identity;
using DigitalBrain.Testing.Module;
using Xunit;

namespace DigitalBrain.Platform.Tests.Unit.Identity;

public sealed class AccountFacts
{
    [Fact]
    public async Task CreatingAWorkspaceUsesAuthenticatedOwnershipAndDeniesOutsiders()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().StartAsync(ct);
        var directory = brain.Get<IIdentityDirectory>(IdentityGrains.Directory);
        var owner = await directory.RegisterAsync("alice", "correct-password", "Alice", ct);
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        var anonymous = await IdentityEndpoints.CreateBrainAsync(http, brain, ct);
        Assert.Equal(401, ((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)anonymous).StatusCode);
        http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, owner.PrincipalId),
            new System.Security.Claims.Claim("intochat.account", owner.AccountId),
        ], "test"));
        var result = await IdentityEndpoints.CreateBrainAsync(http, brain, ct);
        var workspace = Assert.IsType<Member>(((Microsoft.AspNetCore.Http.IValueHttpResult)result).Value);
        Assert.Equal(owner.AccountId, workspace.AccountId);
        Assert.NotEqual(owner.BrainId, workspace.BrainId);
        Assert.True(await directory.CanAccessAsync("alice", workspace.AccountId, workspace.BrainId, ct));
        Assert.False(await directory.CanAccessAsync("bob", workspace.AccountId, workspace.BrainId, ct));
        http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "bob"),
            new System.Security.Claims.Claim("intochat.account", owner.AccountId),
        ], "test"));
        var outsider = await IdentityEndpoints.CreateBrainAsync(http, brain, ct);
        Assert.Equal(403, ((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)outsider).StatusCode);
    }

    [Fact]
    public async Task ReservedPlatformNamesCannotBeRegistered()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().StartAsync(ct);
        var directory = brain.Get<IIdentityDirectory>(IdentityGrains.Directory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => directory.RegisterAsync("integrations", "correct-password", "Intruder", ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => directory.RegisterAsync("owner", "correct-password", "Intruder", ct));
    }

    [Fact]
    public async Task EachRegistrationGetsItsOwnAccountAndBrain()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().StartAsync(ct);
        var directory = brain.Get<IIdentityDirectory>(IdentityGrains.Directory);
        var first = await directory.RegisterAsync("alice", "correct-password", "Alice", ct);
        var second = await directory.RegisterAsync("bob", "another-password", "Bob", ct);
        Assert.NotEqual(first.AccountId, second.AccountId);
        Assert.NotEqual(first.BrainId, second.BrainId);
        Assert.False(await directory.CanAccessAsync("bob", first.AccountId, first.BrainId, ct));
    }

    [Fact]
    public async Task OnlyTheCorrectPasswordAuthenticatesAnExistingAccount()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().StartAsync(ct);
        var directory = brain.Get<IIdentityDirectory>(IdentityGrains.Directory);
        var alice = await directory.RegisterAsync("alice", "correct-password", "Alice", ct);
        Assert.Null(await directory.AuthenticateAsync("alice", "wrong-password", ct));
        Assert.Null(await directory.AuthenticateAsync("alice", "", ct));
        Assert.Null(await directory.AuthenticateAsync("unknown", "correct-password", ct));
        Assert.Equal(alice, await directory.AuthenticateAsync("alice", "correct-password", ct));
    }

    [Fact]
    public async Task RegistrationRefusesWeakPasswordsAndTakenNames()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().StartAsync(ct);
        var directory = brain.Get<IIdentityDirectory>(IdentityGrains.Directory);
        await directory.RegisterAsync("alice", "correct-password", "Alice", ct);
        await Assert.ThrowsAsync<ArgumentException>(() => directory.RegisterAsync("weak", "short", "Weak", ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => directory.RegisterAsync("alice", "new-password", "Pretender", ct));
    }

    [Fact]
    public async Task CanAccessAnswersOnlyForOwnedOrSharedWorkspaces()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().StartAsync(ct);
        var directory = brain.Get<IIdentityDirectory>(IdentityGrains.Directory);
        var owner = await directory.RegisterAsync("alice", "correct-password", "Alice", ct);
        await directory.RegisterAsync("bob", "another-password", "Bob", ct);

        Assert.True(await directory.CanAccessAsync("alice", owner.AccountId, owner.BrainId, ct));
        Assert.False(await directory.CanAccessAsync("alice", owner.AccountId, "elsewhere", ct));
        Assert.False(await directory.CanAccessAsync("bob", owner.AccountId, owner.BrainId, ct));

        await directory.ShareBrainAsync(owner.AccountId, owner.BrainId, "bob", "Bob", MemberRole.Member, ct);
        Assert.True(await directory.CanAccessAsync("bob", owner.AccountId, owner.BrainId, ct));
        Assert.False(await directory.CanAccessAsync("bob", owner.AccountId, "a-brain-that-was-not-shared", ct));
    }
}
