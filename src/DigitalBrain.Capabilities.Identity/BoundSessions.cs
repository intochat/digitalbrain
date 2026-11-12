namespace DigitalBrain.Capabilities.Identity;

using DigitalBrain.Capabilities;

/// <summary>
/// Capability binding for <see cref="ISessions"/> and <see cref="IIdentityProvider"/> over an <see cref="IIdentitySessionPort"/>,
/// resolving the caller via an ambient delegate.
/// </summary>
public sealed class BoundSessions : ISessions, IIdentityProvider
{
    private readonly IIdentitySessionPort _port;
    private readonly Action<string?, CapabilityId, string>? _requirePrivileged;
    private readonly Func<CapabilityCaller?> _caller;

    /// <summary>Creates a new instance of <see cref="BoundSessions"/>.</summary>
    /// <param name="port">The underlying session port.</param>
    /// <param name="caller">The caller resolution delegate.</param>
    /// <param name="requirePrivileged">Optional callback validating privileged callers.</param>
    public BoundSessions(IIdentitySessionPort port, Func<CapabilityCaller?> caller, Action<string?, CapabilityId, string>? requirePrivileged = null)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(caller);
        _port = port;
        _caller = caller;
        _requirePrivileged = requirePrivileged;
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
        var caller = _caller() ?? throw new UnauthorizedAccessException("identity@1: the call carries no caller.");
        _requirePrivileged?.Invoke(caller.AppId, Capability.Identity.V1, member);
        return caller;
    }
}
