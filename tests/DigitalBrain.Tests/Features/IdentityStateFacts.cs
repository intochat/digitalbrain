using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.Identity;
using DigitalBrain.Identity.State;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class IdentityStateFacts
{
    private static readonly VerifiedExternalIdentity Owner = new("owner-bootstrap", "owner");
    private static readonly VerifiedExternalIdentity Telegram = new("telegram", "123456789");

    [Fact]
    public async Task Inconsistent_initial_owner_state_is_rejected_before_any_mutation()
    {
        var storage = new CorruptIdentityStorage();
        await using var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            ConfigureSilo = silo => silo.Services.AddKeyedSingleton<IGrainStorage>(DigitalBrainNames.DefaultGrainStorage, storage)
        });
        var identities = new IdentityService(brain.Grains);
        await Assert.ThrowsAsync<InvalidOperationException>(() => identities.BootstrapOwnerAsync(Owner, "home", TimeSpan.FromHours(1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => identities.BootstrapOwnerAsync(Owner, "home", TimeSpan.FromHours(1)));
        Assert.Equal(0, storage.Writes);
        Assert.Null(storage.State.OwnerUserId);
        Assert.Single(storage.State.Accounts);
        Assert.Single(storage.State.ExternalLinks);
        Assert.False(Assert.IsType<IdentityAccount>(await identities.GetAccountAsync("existing-user")).IsOwner);
    }

    [Fact]
    public async Task Sessions_links_and_automation_revocation_survive_cold_restart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "digitalbrain-tests", Guid.NewGuid().ToString("N"));
        IdentitySession owner;
        IdentitySession linked;
        string grant;
        string code;
        await using (var brain = await Start(directory))
        {
            var service = new IdentityService(brain.Grains);
            Assert.Null(await service.SignInAsync(Telegram, TimeSpan.FromHours(1)));
            owner = await service.BootstrapOwnerAsync(Owner, "home", TimeSpan.FromHours(1));
            Assert.True(owner.IsOwner);
            Assert.Equal(43, owner.Token.Length);
            code = await service.CreateLinkCodeAsync(owner.Token, TimeSpan.FromMinutes(5));
            var attempts = await Task.WhenAll(
                service.RedeemLinkCodeAsync(code, Telegram, TimeSpan.FromHours(1)),
                service.RedeemLinkCodeAsync(code, Telegram, TimeSpan.FromHours(1)));
            linked = Assert.Single(attempts.OfType<IdentitySession>());
            Assert.Equal(owner.UserId, linked.UserId);
            grant = await service.GrantAutomationAsync(owner.Token, "home", "behavior:example", ["counter:test"], ["test.counter/add"]);
            Assert.True(await service.OwnsAutomationGrantAsync(owner.UserId, grant, "home", "behavior:example"));
            Assert.False(await service.IsAutomationAuthorizedAsync(grant, "home", "behavior:other", "counter:test", "test.counter/add"));
            Assert.False(await service.IsAutomationAuthorizedAsync(grant, "other", "behavior:example", "counter:test", "test.counter/add"));
            Assert.False(await service.IsAutomationAuthorizedAsync(grant, "home", "behavior:example", "counter:other", "test.counter/add"));
            Assert.False(await service.IsAutomationAuthorizedAsync(grant, "home", "behavior:example", "counter:test", "test.counter/delete"));
        }

        var durableBytes = string.Concat(Directory.GetFiles(directory, "*.state").Select(path => Encoding.UTF8.GetString(File.ReadAllBytes(path))));
        Assert.DoesNotContain(owner.Token, durableBytes, StringComparison.Ordinal);
        Assert.DoesNotContain(linked.Token, durableBytes, StringComparison.Ordinal);
        Assert.DoesNotContain(code, durableBytes, StringComparison.Ordinal);

        await using (var brain = await Start(directory))
        {
            var service = new IdentityService(brain.Grains);
            var authenticated = Assert.IsType<AuthenticatedIdentity>(await service.AuthenticateAsync(linked.Token));
            Assert.Equal(owner.UserId, authenticated.UserId);
            Assert.Equal(linked.CsrfToken, authenticated.CsrfToken);
            Assert.Equal(WorkspaceRole.Owner, authenticated.Memberships["home"]);
            Assert.Null(await service.RedeemLinkCodeAsync(code, Telegram, TimeSpan.FromHours(1)));
            Assert.Equal(owner.UserId, (await service.SignInAsync(Telegram, TimeSpan.FromHours(1)))?.UserId);
            Assert.True(await service.IsAutomationAuthorizedAsync(grant, "home", "behavior:example", "counter:test", "test.counter/add"));
            await service.RevokeAutomationAsync(owner.Token, grant);
            await service.RevokeSessionAsync(linked.Token);
        }
        await using (var brain = await Start(directory))
        {
            var service = new IdentityService(brain.Grains);
            Assert.Null(await service.AuthenticateAsync(linked.Token));
            Assert.NotNull(await service.AuthenticateAsync(owner.Token));
            Assert.False(await service.IsAutomationAuthorizedAsync(grant, "home", "behavior:example", "counter:test", "test.counter/add"));
        }
    }

    [Fact]
    public async Task Linking_never_takes_over_an_identity_and_revoked_or_expired_sessions_cannot_link()
    {
        var clock = new IdentityClock();
        await using var brain = await Start(null, clock);
        var service = new IdentityService(brain.Grains);
        var owner = await service.BootstrapOwnerAsync(Owner, "home", TimeSpan.FromHours(1));
        var member = await service.CreateMemberAsync(owner.Token, Telegram, "home");
        Assert.False(member.IsOwner);
        var code = await service.CreateLinkCodeAsync(owner.Token, TimeSpan.FromMinutes(5));
        Assert.Null(await service.RedeemLinkCodeAsync(code, Telegram, TimeSpan.FromHours(1)));
        // A rejected takeover does not consume the code; a different verified identity can redeem it.
        Assert.Equal(owner.UserId, (await service.RedeemLinkCodeAsync(code, new("telegram", "different"), TimeSpan.FromHours(1)))?.UserId);
        var memberSession = Assert.IsType<IdentitySession>(await service.SignInAsync(Telegram, TimeSpan.FromMinutes(1)));
        Assert.Equal(member.UserId, memberSession.UserId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GrantAutomationAsync(memberSession.Token, "home", "behavior:x", ["counter:test"], ["test.counter/add"]));
        var revokedCode = await service.CreateLinkCodeAsync(memberSession.Token, TimeSpan.FromMinutes(5));
        await service.RevokeSessionAsync(memberSession.Token);
        Assert.Null(await service.RedeemLinkCodeAsync(revokedCode, new("telegram", "third"), TimeSpan.FromHours(1)));
        var expiredCode = await service.CreateLinkCodeAsync(owner.Token, TimeSpan.FromMinutes(1));
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(await service.RedeemLinkCodeAsync(expiredCode, new("telegram", "fourth"), TimeSpan.FromHours(1)));
        var expiring = Assert.IsType<IdentitySession>(await service.SignInAsync(Telegram, TimeSpan.FromMinutes(1)));
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(await service.AuthenticateAsync(expiring.Token));
    }

    [Fact]
    public async Task Disabled_accounts_and_removed_owner_membership_immediately_deny_durable_grants()
    {
        await using var brain = await Start(null);
        var service = new IdentityService(brain.Grains);
        var owner = await service.BootstrapOwnerAsync(Owner, "home", TimeSpan.FromHours(1));
        var member = await service.CreateMemberAsync(owner.Token, Telegram, "home");
        await service.SetMembershipAsync(owner.Token, "home", member.UserId, WorkspaceRole.Owner);
        var memberSession = Assert.IsType<IdentitySession>(await service.SignInAsync(Telegram, TimeSpan.FromHours(1)));
        var grant = await service.GrantAutomationAsync(memberSession.Token, "home", "behavior:member", ["counter:test"], ["test.counter/add"]);
        Assert.True(await service.IsAutomationAuthorizedAsync(grant, "home", "behavior:member", "counter:test", "test.counter/add"));
        await service.SetMembershipAsync(owner.Token, "home", member.UserId, WorkspaceRole.Member);
        Assert.False(await service.IsAutomationAuthorizedAsync(grant, "home", "behavior:member", "counter:test", "test.counter/add"));
        await service.SetMembershipAsync(owner.Token, "home", member.UserId, WorkspaceRole.Owner);
        await service.SetAccountDisabledAsync(owner.Token, member.UserId, true);
        Assert.Null(await service.AuthenticateAsync(memberSession.Token));
        Assert.Null(await service.SignInAsync(Telegram, TimeSpan.FromHours(1)));
        Assert.False(await service.IsAutomationAuthorizedAsync(grant, "home", "behavior:member", "counter:test", "test.counter/add"));
        await service.SetAccountDisabledAsync(owner.Token, member.UserId, false);
        Assert.Null(await service.AuthenticateAsync(memberSession.Token));
        Assert.False(await service.IsAutomationAuthorizedAsync(grant, "home", "behavior:member", "counter:test", "test.counter/add"));
    }

    private static Task<BrainSimulation> Start(string? directory, TimeProvider? clock = null) => BrainSimulation.StartAsync(new()
    {
        Modules = new([]),
        PersistenceDirectory = directory,
        ConfigureSilo = silo =>
        {
            if (clock is not null)
            {
                silo.Services.AddSingleton(clock);
            }
        }
    });

    private sealed class IdentityClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    private sealed class CorruptIdentityStorage : IGrainStorage
    {
        public IdentityDirectoryState State { get; } = new()
        {
            Accounts = new() { ["existing-user"] = new() },
            ExternalLinks = new() { ["15:owner-bootstrapowner"] = "existing-user" }
        };
        public int Writes { get; private set; }

        public Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
        {
            if (typeof(T) == typeof(IdentityDirectoryState))
            {
                grainState.State = (T)(object)State;
                grainState.RecordExists = true;
            }
            return Task.CompletedTask;
        }

        public Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
        {
            Writes++;
            return Task.CompletedTask;
        }

        public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState) => Task.CompletedTask;
    }
}
