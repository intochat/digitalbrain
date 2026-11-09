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

    private static readonly CapabilityCaller PlatformCaller = CapabilityCallers.Platform("identity.directory", "system", "system");

    /// <inheritdoc />
    public async Task<Account?> ReadAccount(string accountId, CancellationToken cancellationToken = default)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await _port.ReadAccount(accountId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<Account?> OwnerOfBrain(string brainId, CancellationToken cancellationToken = default)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await _port.OwnerOfBrain(brainId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task EnsureAccount(Account account, CancellationToken cancellationToken = default)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            await _port.EnsureAccount(account, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<BrainProvision> CreateBrain(string accountId, string principalId, string operationId, string displayName, BrainKind kind, CancellationToken cancellationToken = default)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await _port.CreateBrain(accountId, principalId, operationId, displayName, kind, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<BrainProvision> EnsureBrain(Member owner, CancellationToken cancellationToken = default)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await _port.EnsureBrain(owner, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<BrainProvision> ProvisionOwner(Member owner, CancellationToken cancellationToken = default)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await _port.ProvisionOwner(owner, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsRegistered(string principalId, CancellationToken cancellationToken = default)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await _port.IsRegistered(principalId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<BrainProvision> RegisterPrincipal(string principalId, string displayName, CancellationToken cancellationToken = default)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await _port.RegisterPrincipal(principalId, displayName, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<BrainProvision?> CompleteRegistration(string principalId, CancellationToken cancellationToken = default)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await _port.CompleteRegistration(principalId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DigitalBrain.Capabilities.Identity.BrainEntry>> BrainsOf(string principalId, CancellationToken cancellationToken = default)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await _port.BrainsOf(principalId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<BrainKind> KindOf(string brainScopeId, CancellationToken cancellationToken = default)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await _port.KindOf(brainScopeId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<bool> BrainExists(string brainScopeId, CancellationToken cancellationToken = default)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await _port.BrainExists(brainScopeId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<Member?> Membership(string brainScopeId, string principalId, CancellationToken cancellationToken = default)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await _port.Membership(brainScopeId, principalId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<Member?> EnsureMember(Member member, CancellationToken cancellationToken = default)
    {
        var caller = RuntimeCallerContext.Required(Capability.Identity.V1Id);
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await _port.EnsureMember(caller, member, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task RemoveMember(string brainScopeId, string accountId, CancellationToken cancellationToken = default)
    {
        var caller = RuntimeCallerContext.Required(Capability.Identity.V1Id);
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            await _port.RemoveMember(caller, brainScopeId, accountId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task RemovePrincipalMember(string brainScopeId, string principalId, CancellationToken cancellationToken = default)
    {
        var caller = RuntimeCallerContext.Required(Capability.Identity.V1Id);
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            await _port.RemovePrincipalMember(caller, brainScopeId, principalId, cancellationToken).ConfigureAwait(false);
        }
    }
}
