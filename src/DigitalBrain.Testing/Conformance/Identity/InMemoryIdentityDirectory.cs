using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;

namespace DigitalBrain.Testing.Conformance.Identity;

/// <summary>
/// An in-memory <see cref="IIdentityDirectoryPort"/> over <see cref="InMemoryIdentity"/>: each caller-taking member runs the
/// reference implementation as the caller it was handed. Lets the runtime's identity@1 capability run its conformance kit
/// without a host directory, and serves as the <see cref="IIdentitySessionPort"/> (sessions unchecked: the runtime checks
/// the privileged grant before it calls the port). Owner-only members answer null/false instead of throwing (the port is neutral).
/// </summary>
public sealed class InMemoryIdentityDirectory : IIdentityDirectoryPort, IIdentitySessionPort
{
    private readonly AmbientCallerScope _callers = new();
    private readonly InMemoryIdentity _identity;

    /// <summary>Creates the directory.</summary>
    /// <param name="time">The clock for created, joined and granted times.</param>
    public InMemoryIdentityDirectory(TimeProvider? time = null) => _identity = new InMemoryIdentity(_callers, time);

    /// <summary>What <see cref="Describe"/> answers; a Secured <c>memory</c> implementation without an account surface by default.</summary>
    public IdentityProviderDescriptor Descriptor { get; init; } = new() { ProviderId = "memory", Posture = IdentityPosture.Secured };

    /// <summary>Simulates a host sign-in (see <see cref="InMemoryIdentity.SignIn"/>).</summary>
    public Task<Member> SignIn(string principalId, CancellationToken cancellationToken) => _identity.SignIn(principalId, cancellationToken);

    /// <summary>Adds a sign-in handler.</summary>
    public void AddSignInHandler(ISignInHandler handler) => _identity.AddSignInHandler(handler);

    /// <inheritdoc />
    public Task<Account?> ReadAccount(string accountId, CancellationToken cancellationToken) => _identity.ReadAccount(accountId, cancellationToken);

    /// <inheritdoc />
    public Task<Account?> OwnerOfBrain(string brainId, CancellationToken cancellationToken) => _identity.OwnerOfBrain(brainId, cancellationToken);

    /// <inheritdoc />
    public Task EnsureAccount(Account account, CancellationToken cancellationToken) => _identity.EnsureAccount(account, cancellationToken);

    /// <inheritdoc />
    public Task<BrainProvision> CreateBrain(string accountId, string principalId, string operationId, string displayName, BrainKind kind, CancellationToken cancellationToken)
        => _identity.CreateBrain(accountId, principalId, operationId, displayName, kind, cancellationToken);

    /// <inheritdoc />
    public Task<BrainProvision> EnsureBrain(Member owner, CancellationToken cancellationToken) => _identity.EnsureBrain(owner, cancellationToken);

    /// <inheritdoc />
    public Task<BrainProvision> ProvisionOwner(Member owner, CancellationToken cancellationToken) => _identity.ProvisionOwner(owner, cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsRegistered(string principalId, CancellationToken cancellationToken) => _identity.IsRegistered(principalId, cancellationToken);

    /// <inheritdoc />
    public Task<BrainProvision> RegisterPrincipal(string principalId, string displayName, CancellationToken cancellationToken)
        => _identity.RegisterPrincipal(principalId, displayName, cancellationToken);

    /// <inheritdoc />
    public Task<BrainProvision?> CompleteRegistration(string principalId, CancellationToken cancellationToken) => _identity.CompleteRegistration(principalId, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<BrainEntry>> BrainsOf(string principalId, CancellationToken cancellationToken) => _identity.BrainsOf(principalId, cancellationToken);

    /// <inheritdoc />
    public Task<BrainKind> KindOf(string brainScopeId, CancellationToken cancellationToken) => _identity.KindOf(brainScopeId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> BrainExists(string brainScopeId, CancellationToken cancellationToken) => _identity.BrainExists(brainScopeId, cancellationToken);

    /// <inheritdoc />
    public Task<Member?> Membership(string brainScopeId, string principalId, CancellationToken cancellationToken) => _identity.Membership(brainScopeId, principalId, cancellationToken);

    /// <inheritdoc />
    public async Task<Member?> EnsureMember(CapabilityCaller caller, Member member, CancellationToken cancellationToken)
    {
        using (As(caller)) { return await _identity.EnsureMember(member, cancellationToken).ConfigureAwait(false); }
    }

    /// <inheritdoc />
    public async Task RemoveMember(CapabilityCaller caller, string brainScopeId, string accountId, CancellationToken cancellationToken)
    {
        using (As(caller)) { await _identity.RemoveMember(brainScopeId, accountId, cancellationToken).ConfigureAwait(false); }
    }

    /// <inheritdoc />
    public async Task RemovePrincipalMember(CapabilityCaller caller, string brainScopeId, string principalId, CancellationToken cancellationToken)
    {
        using (As(caller)) { await _identity.RemovePrincipalMember(brainScopeId, principalId, cancellationToken).ConfigureAwait(false); }
    }

    /// <inheritdoc />
    public async Task<CallDecision?> Authorize(CapabilityCaller caller, CallRequest request, CancellationToken cancellationToken)
    {
        using (As(caller)) { return await _identity.Authorize(request, cancellationToken).ConfigureAwait(false); }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Grant>> ListGrants(string brainScopeId, CancellationToken cancellationToken) => _identity.ListGrants(brainScopeId, cancellationToken);

    /// <inheritdoc />
    public async Task<Grant?> GrantAsOwner(CapabilityCaller caller, Grant grant, CancellationToken cancellationToken)
    {
        using (As(caller))
        {
            try
            {
                return await _identity.GrantAsOwner(grant, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> RevokeAsOwner(CapabilityCaller caller, string appId, string semanticTypeId, GrantMode mode, CancellationToken cancellationToken)
    {
        using (As(caller))
        {
            try
            {
                await _identity.RevokeAsOwner(appId, semanticTypeId, mode, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    public Task<SessionIssued> Issue(CapabilityCaller caller, SessionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return Task.FromResult(_identity.IssueSession(request));
    }

    /// <inheritdoc />
    public Task End(CapabilityCaller caller, string sessionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        _identity.EndSession(sessionId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Member?> Validate(string sessionId, CancellationToken cancellationToken) => Task.FromResult(_identity.ValidateSession(sessionId));

    /// <inheritdoc />
    public IdentityProviderDescriptor Describe() => Descriptor;

    private IDisposable As(CapabilityCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return _callers.As(new CapabilityCaller(caller.PrincipalId, caller.AccountId, caller.BrainId, caller.AppId, caller.ConversationId));
    }
}
