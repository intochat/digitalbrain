namespace DigitalBrain.Capabilities.Identity;

using DigitalBrain.Capabilities;

/// <summary>
/// Capability binding for <see cref="IIdentity"/> and <see cref="IGrants"/> over an <see cref="IIdentityDirectoryPort"/>,
/// resolving the caller via an ambient delegate.
/// </summary>
public sealed class BoundIdentity : IIdentity, IGrants
{
    private readonly IIdentityDirectoryPort _port;
    private readonly Func<CapabilityCaller?> _caller;

    /// <summary>Creates a new instance of <see cref="BoundIdentity"/>.</summary>
    /// <param name="port">The underlying directory port.</param>
    /// <param name="caller">The caller resolution delegate.</param>
    public BoundIdentity(IIdentityDirectoryPort port, Func<CapabilityCaller?> caller)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(caller);
        _port = port;
        _caller = caller;
    }

    private CapabilityCaller RequiredCaller()
        => _caller() ?? throw new UnauthorizedAccessException("identity@1: the call carries no caller.");

    /// <inheritdoc />
    public Task<Account?> ReadAccount(string accountId, CancellationToken cancellationToken = default)
        => _port.ReadAccount(accountId, cancellationToken);

    /// <inheritdoc />
    public Task<Account?> OwnerOfBrain(string brainId, CancellationToken cancellationToken = default)
        => _port.OwnerOfBrain(brainId, cancellationToken);

    /// <inheritdoc />
    public Task EnsureAccount(Account account, CancellationToken cancellationToken = default)
        => _port.EnsureAccount(account, cancellationToken);

    /// <inheritdoc />
    public Task<BrainProvision> CreateBrain(string accountId, string principalId, string operationId, string displayName, BrainKind kind, CancellationToken cancellationToken = default)
        => _port.CreateBrain(accountId, principalId, operationId, displayName, kind, cancellationToken);

    /// <inheritdoc />
    public Task<BrainProvision> EnsureBrain(Member owner, CancellationToken cancellationToken = default)
        => _port.EnsureBrain(owner, cancellationToken);

    /// <inheritdoc />
    public Task<BrainProvision> ProvisionOwner(Member owner, CancellationToken cancellationToken = default)
        => _port.ProvisionOwner(owner, cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsRegistered(string principalId, CancellationToken cancellationToken = default)
        => _port.IsRegistered(principalId, cancellationToken);

    /// <inheritdoc />
    public Task<BrainProvision> RegisterPrincipal(string principalId, string displayName, CancellationToken cancellationToken = default)
        => _port.RegisterPrincipal(principalId, displayName, cancellationToken);

    /// <inheritdoc />
    public Task<BrainProvision?> CompleteRegistration(string principalId, CancellationToken cancellationToken = default)
        => _port.CompleteRegistration(principalId, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<BrainEntry>> BrainsOf(string principalId, CancellationToken cancellationToken = default)
        => _port.BrainsOf(principalId, cancellationToken);

    /// <inheritdoc />
    public Task<BrainKind> KindOf(string brainScopeId, CancellationToken cancellationToken = default)
        => _port.KindOf(brainScopeId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> BrainExists(string brainScopeId, CancellationToken cancellationToken = default)
        => _port.BrainExists(brainScopeId, cancellationToken);

    /// <inheritdoc />
    public Task<Member?> Membership(string brainScopeId, string principalId, CancellationToken cancellationToken = default)
        => _port.Membership(brainScopeId, principalId, cancellationToken);

    /// <inheritdoc />
    public Task<Member?> EnsureMember(Member member, CancellationToken cancellationToken = default)
        => _port.EnsureMember(RequiredCaller(), member, cancellationToken);

    /// <inheritdoc />
    public Task RemoveMember(string brainScopeId, string accountId, CancellationToken cancellationToken = default)
        => _port.RemoveMember(RequiredCaller(), brainScopeId, accountId, cancellationToken);

    /// <inheritdoc />
    public Task RemovePrincipalMember(string brainScopeId, string principalId, CancellationToken cancellationToken = default)
        => _port.RemovePrincipalMember(RequiredCaller(), brainScopeId, principalId, cancellationToken);

    /// <inheritdoc />
    public Task<CallDecision?> Authorize(CallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var caller = _caller();
        if (caller is null)
        {
            return Task.FromResult<CallDecision?>(CallDecision.Deny(CallDenial.NotAuthenticated, "identity@1: the call carries no caller."));
        }

        return _port.Authorize(caller, request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Grant>> ListGrants(string brainScopeId, CancellationToken cancellationToken = default)
        => _port.ListGrants(brainScopeId, cancellationToken);

    /// <inheritdoc />
    public async Task<Grant> GrantAsOwner(Grant grant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grant);
        var caller = RequiredCaller();
        var result = await _port.GrantAsOwner(caller, grant, cancellationToken).ConfigureAwait(false);
        return result ?? throw new UnauthorizedAccessException($"{caller.PrincipalId} is not the owner of {grant.WorkspaceId}.");
    }

    /// <inheritdoc />
    public async Task RevokeAsOwner(string appId, string semanticTypeId, GrantMode mode, CancellationToken cancellationToken = default)
    {
        var caller = RequiredCaller();
        var revoked = await _port.RevokeAsOwner(caller, appId, semanticTypeId, mode, cancellationToken).ConfigureAwait(false);
        if (!revoked)
        {
            throw new UnauthorizedAccessException($"{caller.PrincipalId} is not the owner of {caller.BrainId}.");
        }
    }
}
