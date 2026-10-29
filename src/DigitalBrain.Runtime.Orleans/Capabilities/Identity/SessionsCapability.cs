using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;

namespace DigitalBrain.Runtime.Orleans.Capabilities;

/// <summary>
/// identity@1 sessions and description (<see cref="ISessions"/>, <see cref="IIdentityProvider"/>) over the host's
/// <see cref="IIdentitySessionPort"/> (09-03, D-08, D-15). Every session member reads the caller stamped on the call
/// (<see cref="RuntimeCallerContext"/>) and requires the caller's module to declare the member in its manifest
/// <c>Privileged</c> list (<see cref="ModuleDeclarations"/>); anything else is refused with <see cref="PrivilegeDenied"/>
/// before the port is called (fail closed). Issue checks the request's claims and lifetime first (<see cref="SessionClaims.Validate"/>).
/// </summary>
public sealed class SessionsCapability : ISessions, IIdentityProvider
{
    private readonly IIdentitySessionPort _port;
    private readonly ModuleDeclarations _declarations;

    /// <summary>Creates the capability over <paramref name="port"/>, gated by <paramref name="declarations"/>.</summary>
    public SessionsCapability(IIdentitySessionPort port, ModuleDeclarations declarations)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(declarations);
        _port = port;
        _declarations = declarations;
    }

    /// <inheritdoc />
    public Task<SessionIssued> Issue(SessionRequest request, CancellationToken cancellationToken = default)
    {
        var caller = Privileged(nameof(Issue));
        SessionClaims.Validate(request);
        return _port.Issue(caller, request, cancellationToken);
    }

    /// <inheritdoc />
    public Task End(string sessionId, CancellationToken cancellationToken = default)
    {
        var caller = Privileged(nameof(End));
        ArgumentNullException.ThrowIfNull(sessionId);
        return _port.End(caller, sessionId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Member?> Validate(string sessionId, CancellationToken cancellationToken = default)
    {
        Privileged(nameof(Validate));
        ArgumentNullException.ThrowIfNull(sessionId);
        return _port.Validate(sessionId, cancellationToken);
    }

    /// <inheritdoc />
    public IdentityProviderDescriptor Describe() => _port.Describe();

    private CapabilityCaller Privileged(string member)
    {
        var caller = RuntimeCallerContext.Required(Capability.Identity.V1Id);
        _declarations.RequirePrivileged(caller.AppId, Capability.Identity.V1, member);
        return caller;
    }
}
