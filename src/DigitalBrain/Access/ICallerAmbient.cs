namespace DigitalBrain.Access;

/// <summary>
/// Read side, public. Orleans: one RequestContext key. Simulator: one AsyncLocal.
/// </summary>
public interface ICallerAmbient
{
    /// <summary>The currently active caller, or null when none is established.</summary>
    Caller? Current { get; }
}

/// <summary>
/// Write side, internal: only the kernel and EdgeAuthority enter a caller, so a captured Caller cannot be replayed.
/// </summary>
internal interface ICallerCarrier : ICallerAmbient
{
    IDisposable Enter(Caller? caller);
}
