using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;

namespace DigitalBrain.Runtime.Orleans.Capabilities;

/// <summary>
/// identity@1 grants (<see cref="IGrants"/>) over the host's <see cref="IIdentityDirectoryPort"/> (R4-02, D-09, D-15). Authorize,
/// GrantAsOwner and RevokeAsOwner read the caller stamped on the call (<see cref="RuntimeCallerContext"/>). With no stamped
/// caller Authorize answers <see cref="CallDenial.NotAuthenticated"/> and the owner members throw
/// <see cref="UnauthorizedAccessException"/>; a port refusal (null/false) is <see cref="UnauthorizedAccessException"/>.
/// </summary>
public sealed class GrantsCapability : IGrants
{
    private readonly IIdentityDirectoryPort _port;

    /// <summary>Creates the capability over <paramref name="port"/>.</summary>
    public GrantsCapability(IIdentityDirectoryPort port)
    {
        ArgumentNullException.ThrowIfNull(port);
        _port = port;
    }

    private static readonly CapabilityCaller PlatformCaller = CapabilityCallers.Platform("identity.grants", "system", "system");

    /// <inheritdoc />
    public async Task<CallDecision?> Authorize(CallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var caller = RuntimeCallerContext.Current;
        if (caller is null)
        {
            return CallDecision.Deny(CallDenial.NotAuthenticated, "identity@1: the call carries no stamped caller.");
        }

        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await _port.Authorize(caller, request, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Grant>> ListGrants(string brainScopeId, CancellationToken cancellationToken = default)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await _port.ListGrants(brainScopeId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<Grant> GrantAsOwner(Grant grant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grant);
        var caller = RuntimeCallerContext.Required(Capability.Identity.V1Id);
        Grant? result;
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            result = await _port.GrantAsOwner(caller, grant, cancellationToken).ConfigureAwait(false);
        }

        return result ?? throw new UnauthorizedAccessException($"{caller.PrincipalId} is not the owner of {grant.WorkspaceId}.");
    }

    /// <inheritdoc />
    public async Task RevokeAsOwner(string appId, string semanticTypeId, GrantMode mode, CancellationToken cancellationToken = default)
    {
        var caller = RuntimeCallerContext.Required(Capability.Identity.V1Id);
        bool revoked;
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            revoked = await _port.RevokeAsOwner(caller, appId, semanticTypeId, mode, cancellationToken).ConfigureAwait(false);
        }

        if (!revoked)
        {
            throw new UnauthorizedAccessException($"{caller.PrincipalId} is not the owner of {caller.BrainId}.");
        }
    }
}
