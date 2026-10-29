using DigitalBrain.Capabilities;
namespace DigitalBrain.Capabilities.Identity;

/// <summary>
/// identity@1, second half: grants and call authorization. Same capability id as <see cref="IIdentity"/>.
/// </summary>
/// <remarks>
/// The os <c>CapabilityCaller</c> parameter of <c>GrantAsOwner</c>/<c>RevokeAsOwner</c> and the caller inside
/// <see cref="CallRequest"/> are gone (D-15): the implementation reads the runtime-stamped caller.
/// </remarks>
[Capability("identity", 1)]
public interface IGrants
{
    /// <summary>Authorizes <paramref name="request"/> for the runtime caller; null when no decision applies.</summary>
    Task<CallDecision?> Authorize(CallRequest request, CancellationToken cancellationToken = default);

    /// <summary>The grants recorded in the brain, revoked ones included.</summary>
    Task<IReadOnlyList<Grant>> ListGrants(string brainScopeId, CancellationToken cancellationToken = default);

    /// <summary>Records <paramref name="grant"/> as the brain's owner.</summary>
    /// <exception cref="UnauthorizedAccessException">The runtime caller does not own the brain.</exception>
    Task<Grant> GrantAsOwner(Grant grant, CancellationToken cancellationToken = default);

    /// <summary>Revokes the app's grant for the semantic type and mode as the brain's owner.</summary>
    /// <exception cref="UnauthorizedAccessException">The runtime caller does not own the brain.</exception>
    Task RevokeAsOwner(string appId, string semanticTypeId, GrantMode mode, CancellationToken cancellationToken = default);
}
