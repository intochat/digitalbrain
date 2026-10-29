using DigitalBrain.Capabilities;

namespace DigitalBrain.Capabilities.Identity;

/// <summary>
/// The SPI a host implements so the runtime's identity@1 sessions and description (<see cref="ISessions"/>,
/// <see cref="IIdentityProvider"/>) reach its session store (09-03, D-08, D-15). Separate from
/// <see cref="IIdentityDirectoryPort"/> so existing directory ports keep compiling. The runtime checks the caller's privileged
/// grant and the request's claims before it calls the port; the port stores and answers.
/// </summary>
public interface IIdentitySessionPort
{
    /// <summary>Issues a session for the checked <paramref name="request"/>, on behalf of <paramref name="caller"/>.</summary>
    Task<SessionIssued> Issue(CapabilityCaller caller, SessionRequest request, CancellationToken cancellationToken);

    /// <summary>Ends the session; an unknown session is a no-op.</summary>
    Task End(CapabilityCaller caller, string sessionId, CancellationToken cancellationToken);

    /// <summary>The member of a live session, or null.</summary>
    Task<Member?> Validate(string sessionId, CancellationToken cancellationToken);

    /// <summary>See <see cref="IIdentityProvider.Describe"/>.</summary>
    IdentityProviderDescriptor Describe();
}
