using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Capabilities.Secrets;
using DigitalBrain.Testing.Conformance;
using DigitalBrain.Testing.Conformance.Identity;
using DigitalBrain.Testing.Conformance.Secrets;

namespace DigitalBrain.Testing.Tests.Unit.Conformance;

// The kits test themselves: each deliberately broken subject fails the fact aimed at its fault, and the unbroken
// decorators pass the same facts (so a failure is the fault, not the harness).
public sealed class BrokenSecretsIdentityFacts
{
    [Fact]
    public Task Value_in_the_ref_fails_the_redaction_fact() =>
        Assert.ThrowsAnyAsync<Exception>(() => new BrokenSecretsKit(SecretsFault.ValueInRef).SetReturnsRefWithNameAndNoValue());

    [Fact]
    public Task Value_in_the_log_fails_the_log_fact() =>
        Assert.ThrowsAnyAsync<Exception>(() => new BrokenSecretsKit(SecretsFault.ValueInLog).LogsNeverContainValue());

    [Fact]
    public Task Cross_brain_resolve_fails_the_denied_fact() =>
        Assert.ThrowsAnyAsync<Exception>(() => new BrokenSecretsKit(SecretsFault.IgnoreCaller).OtherCallersSecretIsDenied());

    [Fact]
    public Task Ignored_revoke_fails_the_revoke_fact() =>
        Assert.ThrowsAnyAsync<Exception>(() => new BrokenIdentityKit(IdentityFault.IgnoreRevoke).RevokeThenAuthorizeDenies());

    [Fact]
    public Task Unpersisted_membership_fails_the_membership_fact() =>
        Assert.ThrowsAnyAsync<Exception>(() => new BrokenIdentityKit(IdentityFault.ForgetMembers).EnsureMemberThenMembershipContainsIt());

    [Fact]
    public Task Ungated_grant_fails_the_owner_fact() =>
        Assert.ThrowsAnyAsync<Exception>(() => new BrokenIdentityKit(IdentityFault.AnyoneGrants).GrantAsOwnerByNonOwnerThrowsUnauthorized());

    [Fact]
    public async Task The_unbroken_decorators_pass_the_same_facts()
    {
        var secrets = new BrokenSecretsKit(SecretsFault.None);
        await secrets.SetReturnsRefWithNameAndNoValue();
        await secrets.LogsNeverContainValue();
        await secrets.OtherCallersSecretIsDenied();
        var identity = new BrokenIdentityKit(IdentityFault.None);
        await identity.RevokeThenAuthorizeDenies();
        await identity.EnsureMemberThenMembershipContainsIt();
        await identity.GrantAsOwnerByNonOwnerThrowsUnauthorized();
    }

    internal enum SecretsFault { None, ValueInRef, ValueInLog, IgnoreCaller }

    internal enum IdentityFault { None, IgnoreRevoke, ForgetMembers, AnyoneGrants }

    private sealed class BrokenSecretsKit(SecretsFault fault) : SecretsConformance<ISecrets>
    {
        protected override Task<SecretsSubject> Connect(CancellationToken ct)
        {
            var callers = new AmbientCallerScope();
            var inner = new InMemorySecrets(callers);
            var broken = new BrokenSecrets(inner, fault);
            return Task.FromResult(new SecretsSubject(broken, callers, () => [.. inner.Log, .. broken.Log]));
        }
    }

    private sealed class BrokenSecrets(InMemorySecrets inner, SecretsFault fault) : ISecrets
    {
        private readonly List<string> _log = [];
        private readonly Dictionary<string, string> _byReference = [];

        public IReadOnlyList<string> Log => _log;

        public async Task<SecretRef> Set(string name, string label, string value, CancellationToken cancellationToken = default)
        {
            var secret = await inner.Set(name, label, value, cancellationToken);
            _byReference[secret.Reference] = value;
            if (fault == SecretsFault.ValueInLog) { _log.Add($"set {name}={value}"); }
            return fault == SecretsFault.ValueInRef ? secret with { Reference = $"{secret.Reference}:{value}" } : secret;
        }

        public Task Remove(string name, CancellationToken cancellationToken = default) => inner.Remove(name, cancellationToken);

        public Task<string> Resolve(SecretRef secret, CancellationToken cancellationToken = default) =>
            fault == SecretsFault.IgnoreCaller && _byReference.TryGetValue(secret.Reference, out var value)
                ? Task.FromResult(value)
                : inner.Resolve(secret, cancellationToken);

        public Task<SealedSecret> Seal(string context, ReadOnlyMemory<byte> plaintext, CancellationToken cancellationToken = default) => inner.Seal(context, plaintext, cancellationToken);

        public Task<byte[]> Open(string context, SealedSecret sealedSecret, CancellationToken cancellationToken = default) => inner.Open(context, sealedSecret, cancellationToken);

        public Task<SecretRef> Named(string name, CancellationToken cancellationToken = default) => inner.Named(name, cancellationToken);
    }

    private sealed class BrokenIdentityKit(IdentityFault fault) : IdentityConformance<IIdentity>
    {
        protected override Task<IdentitySubject> Connect(CancellationToken ct)
        {
            var callers = new AmbientCallerScope();
            var inner = new InMemoryIdentity(callers);
            return Task.FromResult(new IdentitySubject(new BrokenIdentity(inner, fault), new BrokenGrants(inner, fault), callers, inner.SignIn));
        }
    }

    private sealed class BrokenGrants(InMemoryIdentity inner, IdentityFault fault) : IGrants
    {
        public Task<CallDecision?> Authorize(CallRequest request, CancellationToken cancellationToken = default) => inner.Authorize(request, cancellationToken);

        public Task<IReadOnlyList<Grant>> ListGrants(string brainScopeId, CancellationToken cancellationToken = default) => inner.ListGrants(brainScopeId, cancellationToken);

        public Task<Grant> GrantAsOwner(Grant grant, CancellationToken cancellationToken = default) =>
            fault == IdentityFault.AnyoneGrants ? Task.FromResult(grant) : inner.GrantAsOwner(grant, cancellationToken);

        public Task RevokeAsOwner(string appId, string semanticTypeId, GrantMode mode, CancellationToken cancellationToken = default) =>
            fault == IdentityFault.IgnoreRevoke ? Task.CompletedTask : inner.RevokeAsOwner(appId, semanticTypeId, mode, cancellationToken);
    }

    private sealed class BrokenIdentity(InMemoryIdentity inner, IdentityFault fault) : IIdentity
    {
        public Task<Account?> ReadAccount(string accountId, CancellationToken cancellationToken = default) => inner.ReadAccount(accountId, cancellationToken);

        public Task<Account?> OwnerOfBrain(string brainId, CancellationToken cancellationToken = default) => inner.OwnerOfBrain(brainId, cancellationToken);

        public Task EnsureAccount(Account account, CancellationToken cancellationToken = default) => inner.EnsureAccount(account, cancellationToken);

        public Task<BrainProvision> CreateBrain(string accountId, string principalId, string operationId, string displayName, BrainKind kind, CancellationToken cancellationToken = default) =>
            inner.CreateBrain(accountId, principalId, operationId, displayName, kind, cancellationToken);

        public Task<BrainProvision> EnsureBrain(Member owner, CancellationToken cancellationToken = default) => inner.EnsureBrain(owner, cancellationToken);

        public Task<BrainProvision> ProvisionOwner(Member owner, CancellationToken cancellationToken = default) => inner.ProvisionOwner(owner, cancellationToken);

        public Task<bool> IsRegistered(string principalId, CancellationToken cancellationToken = default) => inner.IsRegistered(principalId, cancellationToken);

        public Task<BrainProvision> RegisterPrincipal(string principalId, string displayName, CancellationToken cancellationToken = default) =>
            inner.RegisterPrincipal(principalId, displayName, cancellationToken);

        public Task<BrainProvision?> CompleteRegistration(string principalId, CancellationToken cancellationToken = default) => inner.CompleteRegistration(principalId, cancellationToken);

        public Task<IReadOnlyList<BrainEntry>> BrainsOf(string principalId, CancellationToken cancellationToken = default) => inner.BrainsOf(principalId, cancellationToken);

        public Task<BrainKind> KindOf(string brainScopeId, CancellationToken cancellationToken = default) => inner.KindOf(brainScopeId, cancellationToken);

        public Task<bool> BrainExists(string brainScopeId, CancellationToken cancellationToken = default) => inner.BrainExists(brainScopeId, cancellationToken);

        public Task<Member?> Membership(string brainScopeId, string principalId, CancellationToken cancellationToken = default) => inner.Membership(brainScopeId, principalId, cancellationToken);

        public Task<Member?> EnsureMember(Member member, CancellationToken cancellationToken = default) =>
            fault == IdentityFault.ForgetMembers ? Task.FromResult<Member?>(member) : inner.EnsureMember(member, cancellationToken);

        public Task RemoveMember(string brainScopeId, string accountId, CancellationToken cancellationToken = default) => inner.RemoveMember(brainScopeId, accountId, cancellationToken);

        public Task RemovePrincipalMember(string brainScopeId, string principalId, CancellationToken cancellationToken = default) => inner.RemovePrincipalMember(brainScopeId, principalId, cancellationToken);
    }
}
