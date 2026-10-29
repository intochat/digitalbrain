namespace DigitalBrain.Capabilities.Identity;

/// <summary>
/// identity@1, first half: accounts, brains, principals and membership. <see cref="IGrants"/> is the second half under
/// the same capability id; one connection provides both (D-09).
/// </summary>
/// <remarks>
/// No member takes a caller (D-15): gated members (<see cref="EnsureMember"/>, <see cref="RemoveMember"/>,
/// <see cref="RemovePrincipalMember"/>) check the caller the runtime stamped on the call. Sessions are the
/// separate <see cref="ISessions"/> under the same id; the cookie write stays with the host HTTP edge. Later versions add members
/// additively.
/// </remarks>
[Capability("identity", 1)]
public interface IIdentity
{
    /// <summary>The account, or null.</summary>
    Task<Account?> ReadAccount(string accountId, CancellationToken cancellationToken = default);

    /// <summary>The account that owns the brain, or null.</summary>
    Task<Account?> OwnerOfBrain(string brainId, CancellationToken cancellationToken = default);

    /// <summary>Creates the account when it does not exist.</summary>
    Task EnsureAccount(Account account, CancellationToken cancellationToken = default);

    /// <summary>Creates a brain of <paramref name="kind"/> for the account; a circle is created for its creator's account.</summary>
    Task<BrainProvision> CreateBrain(string accountId, string principalId, string operationId, string displayName, BrainKind kind, CancellationToken cancellationToken = default);

    /// <summary>Ensures the owner's brain exists (idempotent).</summary>
    Task<BrainProvision> EnsureBrain(Member owner, CancellationToken cancellationToken = default);

    /// <summary>Provisions the owner: account, brain and membership (idempotent).</summary>
    Task<BrainProvision> ProvisionOwner(Member owner, CancellationToken cancellationToken = default);

    /// <summary>Whether the principal has registered.</summary>
    Task<bool> IsRegistered(string principalId, CancellationToken cancellationToken = default);

    /// <summary>Registers the principal and provisions its personal brain.</summary>
    Task<BrainProvision> RegisterPrincipal(string principalId, string displayName, CancellationToken cancellationToken = default);

    /// <summary>Completes a pending registration, or null when none is pending.</summary>
    Task<BrainProvision?> CompleteRegistration(string principalId, CancellationToken cancellationToken = default);

    /// <summary>The brains the principal holds a membership for, owned or shared.</summary>
    Task<IReadOnlyList<BrainEntry>> BrainsOf(string principalId, CancellationToken cancellationToken = default);

    /// <summary>The brain's kind; <see cref="BrainKind.Personal"/> for an id nobody created.</summary>
    Task<BrainKind> KindOf(string brainScopeId, CancellationToken cancellationToken = default);

    /// <summary>Whether the brain exists (its owner was provisioned).</summary>
    Task<bool> BrainExists(string brainScopeId, CancellationToken cancellationToken = default);

    /// <summary>The principal's membership of the brain, or null.</summary>
    Task<Member?> Membership(string brainScopeId, string principalId, CancellationToken cancellationToken = default);

    /// <summary>Adds the member when the runtime caller may; null when it may not.</summary>
    Task<Member?> EnsureMember(Member member, CancellationToken cancellationToken = default);

    /// <summary>Removes every membership of the account in the brain; the owner's account is never removed.</summary>
    Task RemoveMember(string brainScopeId, string accountId, CancellationToken cancellationToken = default);

    /// <summary>Removes one non-owner member by principal id; the owner throws, an unknown principal is a no-op.</summary>
    Task RemovePrincipalMember(string brainScopeId, string principalId, CancellationToken cancellationToken = default);
}
