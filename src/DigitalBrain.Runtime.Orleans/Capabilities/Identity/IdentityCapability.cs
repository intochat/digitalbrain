using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;

namespace DigitalBrain.Runtime.Orleans.Capabilities;

/// <summary>
/// identity@1 (<see cref="IIdentity"/>) over the host's <see cref="IIdentityDirectoryPort"/> (R4-02, D-09, D-15). A thin
/// adapter: lookups and provisioning delegate as-is; the gated members (<see cref="EnsureMember"/>, <see cref="RemoveMember"/>,
/// <see cref="RemovePrincipalMember"/>) read the caller stamped on the call (<see cref="RuntimeCallerContext"/>) and hand it to
/// the port. A gated call with no stamped caller is denied (<see cref="UnauthorizedAccessException"/>), never defaulted.
/// </summary>
public sealed class IdentityCapability : IIdentity
{
    private readonly IIdentityDirectoryPort _port;

    /// <summary>Creates the capability over <paramref name="port"/>.</summary>
    public IdentityCapability(IIdentityDirectoryPort port)
    {
        ArgumentNullException.ThrowIfNull(port);
        _port = port;
    }

    /// <inheritdoc />
    public Task<Account?> ReadAccount(string accountId, CancellationToken cancellationToken = default) => _port.ReadAccount(accountId, cancellationToken);

    /// <inheritdoc />
    public Task<Account?> OwnerOfBrain(string brainId, CancellationToken cancellationToken = default) => _port.OwnerOfBrain(brainId, cancellationToken);

    /// <inheritdoc />
    public Task EnsureAccount(Account account, CancellationToken cancellationToken = default) => _port.EnsureAccount(account, cancellationToken);

    /// <inheritdoc />
    public Task<BrainProvision> CreateBrain(string accountId, string principalId, string operationId, string displayName, BrainKind kind, CancellationToken cancellationToken = default)
        => _port.CreateBrain(accountId, principalId, operationId, displayName, kind, cancellationToken);

    /// <inheritdoc />
    public Task<BrainProvision> EnsureBrain(Member owner, CancellationToken cancellationToken = default) => _port.EnsureBrain(owner, cancellationToken);

    /// <inheritdoc />
    public Task<BrainProvision> ProvisionOwner(Member owner, CancellationToken cancellationToken = default) => _port.ProvisionOwner(owner, cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsRegistered(string principalId, CancellationToken cancellationToken = default) => _port.IsRegistered(principalId, cancellationToken);

    /// <inheritdoc />
    public Task<BrainProvision> RegisterPrincipal(string principalId, string displayName, CancellationToken cancellationToken = default)
        => _port.RegisterPrincipal(principalId, displayName, cancellationToken);

    /// <inheritdoc />
    public Task<BrainProvision?> CompleteRegistration(string principalId, CancellationToken cancellationToken = default) => _port.CompleteRegistration(principalId, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<DigitalBrain.Capabilities.Identity.BrainEntry>> BrainsOf(string principalId, CancellationToken cancellationToken = default) => _port.BrainsOf(principalId, cancellationToken);

    /// <inheritdoc />
    public Task<BrainKind> KindOf(string brainScopeId, CancellationToken cancellationToken = default) => _port.KindOf(brainScopeId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> BrainExists(string brainScopeId, CancellationToken cancellationToken = default) => _port.BrainExists(brainScopeId, cancellationToken);

    /// <inheritdoc />
    public Task<Member?> Membership(string brainScopeId, string principalId, CancellationToken cancellationToken = default) => _port.Membership(brainScopeId, principalId, cancellationToken);

    /// <inheritdoc />
    public Task<Member?> EnsureMember(Member member, CancellationToken cancellationToken = default)
        => _port.EnsureMember(RuntimeCallerContext.Required(Capability.Identity.V1Id), member, cancellationToken);

    /// <inheritdoc />
    public Task RemoveMember(string brainScopeId, string accountId, CancellationToken cancellationToken = default)
        => _port.RemoveMember(RuntimeCallerContext.Required(Capability.Identity.V1Id), brainScopeId, accountId, cancellationToken);

    /// <inheritdoc />
    public Task RemovePrincipalMember(string brainScopeId, string principalId, CancellationToken cancellationToken = default)
        => _port.RemovePrincipalMember(RuntimeCallerContext.Required(Capability.Identity.V1Id), brainScopeId, principalId, cancellationToken);
}
