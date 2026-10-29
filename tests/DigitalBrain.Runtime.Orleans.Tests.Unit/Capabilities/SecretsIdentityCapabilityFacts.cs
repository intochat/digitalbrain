using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Capabilities.Secrets;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Testing.Conformance.Identity;
using DigitalBrain.Testing.Conformance.Secrets;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Capabilities;

public sealed class SecretsIdentityCapabilityFacts
{
    private static readonly CapabilityCaller Ada = new("ada", "ada-account", "ada-brain");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Secrets_without_a_stamped_caller_are_denied_not_defaulted()
    {
        var vault = new InMemorySecretVault();
        var secrets = new SecretsCapability(vault);
        Assert.Null(RuntimeCallerContext.Current);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => secrets.Set("k", "Key", "v", Ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => secrets.Remove("k", Ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => secrets.Resolve(new SecretRef { Reference = "ada-brain/k", Status = SecretStatus.Set }, Ct));
        Assert.Empty(vault.Callers);
    }

    [Fact]
    public async Task Secrets_pass_the_stamped_caller_to_the_port_and_restore_it_after()
    {
        var vault = new InMemorySecretVault();
        var secrets = new SecretsCapability(vault);
        using (RuntimeCallerContext.Stamp(Ada))
        {
            var secret = await secrets.Set("k", "Key", "v", Ct);
            Assert.Equal("v", await secrets.Resolve(secret, Ct));
        }

        Assert.Null(RuntimeCallerContext.Current);
        Assert.All(vault.Callers, caller => Assert.Equal(Ada, caller));
        Assert.Equal(2, vault.Callers.Count);
    }

    [Fact]
    public async Task Port_not_found_and_denied_map_to_contract_errors()
    {
        var port = new FixedVault(SecretVaultResult.Denied);
        var secrets = new SecretsCapability(port);
        var reference = new SecretRef { Reference = "x/k", Status = SecretStatus.Set };
        using (RuntimeCallerContext.Stamp(Ada))
        {
            Assert.Equal("denied", (await Assert.ThrowsAsync<SecretDenied>(() => secrets.Resolve(reference, Ct))).Code);
            await Assert.ThrowsAsync<SecretDenied>(() => secrets.Set("k", "Key", "v", Ct));
            port.Answer = SecretVaultResult.NotFound;
            Assert.Equal("not-found", (await Assert.ThrowsAsync<SecretNotFound>(() => secrets.Resolve(reference, Ct))).Code);
        }
    }

    [Fact]
    public async Task Identity_gated_members_without_a_stamped_caller_are_denied()
    {
        var directory = new InMemoryIdentityDirectory();
        var identity = new IdentityCapability(directory);
        var grants = new GrantsCapability(directory);
        var owner = (await identity.RegisterPrincipal("ada", "Ada", Ct)).Member;
        var guest = owner with { PrincipalId = "bob", AccountId = "bob-account", Role = MemberRole.Member };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => identity.EnsureMember(guest, Ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => identity.RemoveMember(owner.BrainId, "bob-account", Ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => identity.RemovePrincipalMember(owner.BrainId, "bob", Ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => grants.GrantAsOwner(new Grant { AppId = "a", SemanticTypeId = "t", Mode = GrantMode.Always, WorkspaceId = owner.BrainId }, Ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => grants.RevokeAsOwner("a", "t", GrantMode.Always, Ct));
        var decision = await grants.Authorize(new CallRequest { TargetNeuron = "n", Operation = "o" }, Ct);
        Assert.NotNull(decision);
        Assert.False(decision.Allowed);
        Assert.Equal(CallDenial.NotAuthenticated, decision.Denial);
        Assert.Null(await identity.Membership(owner.BrainId, "bob", Ct));
    }

    [Fact]
    public async Task Grant_as_owner_uses_the_stamped_caller_not_a_parameter()
    {
        var directory = new InMemoryIdentityDirectory();
        var identity = new IdentityCapability(directory);
        var grants = new GrantsCapability(directory);
        var owner = (await identity.RegisterPrincipal("ada", "Ada", Ct)).Member;
        var grant = new Grant { AppId = "a", SemanticTypeId = "t", Mode = GrantMode.Always, WorkspaceId = owner.BrainId };

        using (RuntimeCallerContext.Stamp(new CapabilityCaller("mallory", "m-account", owner.BrainId)))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => grants.GrantAsOwner(grant, Ct));
        }

        using (RuntimeCallerContext.Stamp(new CapabilityCaller(owner.PrincipalId, owner.AccountId, owner.BrainId)))
        {
            Assert.True((await grants.GrantAsOwner(grant, Ct)).Covers("a", "t", owner.BrainId, null));
        }
    }

    [Fact]
    public async Task Registration_is_keyed_under_the_capability_ids_and_the_broker_resolves_it()
    {
        var services = new ServiceCollection()
            .AddSecretsCapability<InMemorySecretVault>()
            .AddIdentityCapability<InMemoryIdentityDirectory>();
        await using var provider = services.BuildServiceProvider();
        var broker = new CapabilityBroker(new CapabilityRegistry(), new CapabilityBrokerFacts.FakeConnections(), provider, []);

        Assert.IsType<SecretsCapability>(await broker.Resolve<ISecrets>(new BrainId("b"), Ct));
        Assert.IsType<IdentityCapability>(await broker.Resolve<IIdentity>(new BrainId("b"), Ct));
        Assert.IsType<GrantsCapability>(await broker.Resolve<IGrants>(new BrainId("b"), Ct));
        Assert.Same(provider.GetKeyedService<ISecrets>(Capability.Secrets.V1Id), await broker.Resolve<ISecrets>(new BrainId("b"), Ct));
        Assert.Empty(CapabilityBroker.SingleRegistrationIssues(new CapabilityRegistry(), services));
    }

    [Fact]
    public void A_second_registration_is_a_startup_issue()
    {
        var services = new ServiceCollection()
            .AddSecretsCapability<InMemorySecretVault>()
            .AddSecretsCapability<InMemorySecretVault>()
            .AddIdentityCapability<InMemoryIdentityDirectory>()
            .AddIdentityCapability<InMemoryIdentityDirectory>();

        var issues = CapabilityBroker.SingleRegistrationIssues(new CapabilityRegistry(), services);

        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, issue => issue.Contains("secrets@1", StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Contains("identity@1", StringComparison.Ordinal));
    }

    private sealed class FixedVault(SecretVaultResult answer) : ISecretVaultPort
    {
        public SecretVaultResult Answer { get; set; } = answer;

        public Task<SecretVaultResult> Set(CapabilityCaller caller, string name, string label, string value, CancellationToken cancellationToken) => Task.FromResult(Answer);

        public Task<SecretVaultStatus> Remove(CapabilityCaller caller, string name, CancellationToken cancellationToken) => Task.FromResult(Answer.Status);

        public Task<SecretVaultResult> Resolve(CapabilityCaller caller, SecretRef secret, CancellationToken cancellationToken) => Task.FromResult(Answer);
    }
}
