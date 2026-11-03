using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;

namespace DigitalBrain.Identity.Local.Directory;

/// <summary>Implements <see cref="IIdentityDirectoryPort"/> and <see cref="IIdentitySessionPort"/> over <see cref="DirectoryNeuron"/>.</summary>
public sealed class LocalDirectoryPort(Func<Type, string, object> neuronResolver, TimeProvider? time = null)
    : IIdentityDirectoryPort, IIdentitySessionPort
{
    private static readonly IdentityProviderDescriptor LocalDescriptor = new()
    {
        ProviderId = "local",
        Posture = IdentityPosture.Open,
    };

    private readonly Func<Type, string, object> _neuronResolver = neuronResolver ?? throw new ArgumentNullException(nameof(neuronResolver));
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <inheritdoc />
    public Task<Account?> ReadAccount(string accountId, CancellationToken cancellationToken)
        => Directory().ReadAccount(accountId).WaitAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Account?> OwnerOfBrain(string brainId, CancellationToken cancellationToken)
        => Directory().OwnerOfBrain(brainId).WaitAsync(cancellationToken);

    /// <inheritdoc />
    public Task EnsureAccount(Account account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        var stamped = account.CreatedAt == default ? account with { CreatedAt = _time.GetUtcNow() } : account;
        return Directory().EnsureAccount(stamped).WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<BrainProvision> CreateBrain(
        string accountId,
        string principalId,
        string operationId,
        string displayName,
        BrainKind kind,
        CancellationToken cancellationToken)
        => Directory().CreateBrain(accountId, principalId, operationId, displayName, kind).WaitAsync(cancellationToken);

    /// <inheritdoc />
    public Task<BrainProvision> EnsureBrain(Member owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return Directory().EnsureBrain(owner).WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<BrainProvision> ProvisionOwner(Member owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return Directory().ProvisionOwner(owner).WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> IsRegistered(string principalId, CancellationToken cancellationToken)
        => Directory().IsRegistered(principalId).WaitAsync(cancellationToken);

    /// <inheritdoc />
    public Task<BrainProvision> RegisterPrincipal(string principalId, string displayName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        return Directory().RegisterPrincipal(principalId, displayName).WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<BrainProvision?> CompleteRegistration(string principalId, CancellationToken cancellationToken)
        => Directory().CompleteRegistration(principalId).WaitAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrainEntry>> BrainsOf(string principalId, CancellationToken cancellationToken)
        => await Directory().BrainsOf(principalId).WaitAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<BrainKind> KindOf(string brainScopeId, CancellationToken cancellationToken)
        => Directory().KindOf(brainScopeId).WaitAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> BrainExists(string brainScopeId, CancellationToken cancellationToken)
        => Directory().BrainExists(brainScopeId).WaitAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Member?> Membership(string brainScopeId, string principalId, CancellationToken cancellationToken)
        => Directory().Membership(brainScopeId, principalId).WaitAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Member?> EnsureMember(CapabilityCaller caller, Member member, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(member);
        return Directory().EnsureMember(caller, member).WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task RemoveMember(CapabilityCaller caller, string brainScopeId, string accountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return Directory().RemoveMember(caller, brainScopeId, accountId).WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task RemovePrincipalMember(CapabilityCaller caller, string brainScopeId, string principalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return Directory().RemovePrincipalMember(caller, brainScopeId, principalId).WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<CallDecision?> Authorize(CapabilityCaller caller, CallRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(request);
        return Directory().Authorize(caller, request).WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Grant>> ListGrants(string brainScopeId, CancellationToken cancellationToken)
        => await Directory().ListGrants(brainScopeId).WaitAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<Grant?> GrantAsOwner(CapabilityCaller caller, Grant grant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(grant);
        return Directory().GrantAsOwner(caller, grant).WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> RevokeAsOwner(
        CapabilityCaller caller,
        string appId,
        string semanticTypeId,
        GrantMode mode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return Directory().RevokeAsOwner(caller, appId, semanticTypeId, mode).WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<SessionIssued> Issue(CapabilityCaller caller, SessionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        SessionClaims.Validate(request);
        return Directory().Issue(request).WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task End(CapabilityCaller caller, string sessionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(sessionId);
        return Directory().End(sessionId).WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<Member?> Validate(string sessionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        return Directory().Validate(sessionId).WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public IdentityProviderDescriptor Describe() => LocalDescriptor;

    private IDirectory Directory()
        => (IDirectory)_neuronResolver(typeof(IDirectory), IDirectory.Key);
}
