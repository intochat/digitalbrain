using DigitalBrain.Capabilities;

namespace DigitalBrain.Capabilities.Identity;

/// <summary>
/// The SPI a host implements so the runtime's identity@1 capability (<see cref="IIdentity"/> and <see cref="IGrants"/>) reaches
/// its identity directory (R4-02, D-09, D-15). Members that act for someone take the caller the runtime read from the stamped
/// call context; the rest mirror <see cref="IIdentity"/>. Neutral DigitalBrain records only. Sessions and the
/// description use the separate <see cref="IIdentitySessionPort"/> (09-03).
/// </summary>
public interface IIdentityDirectoryPort
{
    /// <summary>See <see cref="IIdentity.ReadAccount"/>.</summary>
    Task<Account?> ReadAccount(string accountId, CancellationToken cancellationToken);

    /// <summary>See <see cref="IIdentity.OwnerOfBrain"/>.</summary>
    Task<Account?> OwnerOfBrain(string brainId, CancellationToken cancellationToken);

    /// <summary>See <see cref="IIdentity.EnsureAccount"/>.</summary>
    Task EnsureAccount(Account account, CancellationToken cancellationToken);

    /// <summary>See <see cref="IIdentity.CreateBrain"/>.</summary>
    Task<BrainProvision> CreateBrain(string accountId, string principalId, string operationId, string displayName, BrainKind kind, CancellationToken cancellationToken);

    /// <summary>See <see cref="IIdentity.EnsureBrain"/>.</summary>
    Task<BrainProvision> EnsureBrain(Member owner, CancellationToken cancellationToken);

    /// <summary>See <see cref="IIdentity.ProvisionOwner"/>.</summary>
    Task<BrainProvision> ProvisionOwner(Member owner, CancellationToken cancellationToken);

    /// <summary>See <see cref="IIdentity.IsRegistered"/>.</summary>
    Task<bool> IsRegistered(string principalId, CancellationToken cancellationToken);

    /// <summary>See <see cref="IIdentity.RegisterPrincipal"/>.</summary>
    Task<BrainProvision> RegisterPrincipal(string principalId, string displayName, CancellationToken cancellationToken);

    /// <summary>See <see cref="IIdentity.CompleteRegistration"/>.</summary>
    Task<BrainProvision?> CompleteRegistration(string principalId, CancellationToken cancellationToken);

    /// <summary>See <see cref="IIdentity.BrainsOf"/>.</summary>
    Task<IReadOnlyList<BrainEntry>> BrainsOf(string principalId, CancellationToken cancellationToken);

    /// <summary>See <see cref="IIdentity.KindOf"/>.</summary>
    Task<BrainKind> KindOf(string brainScopeId, CancellationToken cancellationToken);

    /// <summary>See <see cref="IIdentity.BrainExists"/>.</summary>
    Task<bool> BrainExists(string brainScopeId, CancellationToken cancellationToken);

    /// <summary>See <see cref="IIdentity.Membership"/>.</summary>
    Task<Member?> Membership(string brainScopeId, string principalId, CancellationToken cancellationToken);

    /// <summary>Adds the member when <paramref name="caller"/> may; null when it may not.</summary>
    Task<Member?> EnsureMember(CapabilityCaller caller, Member member, CancellationToken cancellationToken);

    /// <summary>Removes the account's memberships as <paramref name="caller"/>; never the owner.</summary>
    Task RemoveMember(CapabilityCaller caller, string brainScopeId, string accountId, CancellationToken cancellationToken);

    /// <summary>Removes one non-owner member as <paramref name="caller"/>; an unknown principal is a no-op.</summary>
    Task RemovePrincipalMember(CapabilityCaller caller, string brainScopeId, string principalId, CancellationToken cancellationToken);

    /// <summary>Authorizes <paramref name="request"/> for <paramref name="caller"/>; null when no decision applies.</summary>
    Task<CallDecision?> Authorize(CapabilityCaller caller, CallRequest request, CancellationToken cancellationToken);

    /// <summary>See <see cref="IGrants.ListGrants"/>.</summary>
    Task<IReadOnlyList<Grant>> ListGrants(string brainScopeId, CancellationToken cancellationToken);

    /// <summary>Records <paramref name="grant"/> when <paramref name="caller"/> owns its brain; null when it does not.</summary>
    Task<Grant?> GrantAsOwner(CapabilityCaller caller, Grant grant, CancellationToken cancellationToken);

    /// <summary>Revokes in the caller's brain when <paramref name="caller"/> owns it; false when it does not.</summary>
    Task<bool> RevokeAsOwner(CapabilityCaller caller, string appId, string semanticTypeId, GrantMode mode, CancellationToken cancellationToken);
}
