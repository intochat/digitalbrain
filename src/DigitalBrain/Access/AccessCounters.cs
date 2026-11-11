namespace DigitalBrain.Access;

/// <summary>
/// Observability callbacks for access gating telemetry.
/// </summary>
public sealed class AccessCounters
{
    /// <summary>Fired when an invocation would be denied under strict mode but was admitted due to audit exemption.</summary>
    public Action<string, string, AccessDenial>? OnWouldDeny { get; set; }

    /// <summary>Fired when an invocation was admitted as implicit kernel.</summary>
    public Action<string, string>? OnImplicitKernel { get; set; }

    /// <summary>Fired when a legacy caller stamp was processed.</summary>
    public Action<string>? OnLegacyStamp { get; set; }

    /// <summary>Records an access denial prevented by an audit exemption.</summary>
    public void WouldDeny(string contract, string method, AccessDenial denial)
        => OnWouldDeny?.Invoke(contract, method, denial);

    /// <summary>Records an implicit kernel admission.</summary>
    public void ImplicitKernel(string contract, string method)
        => OnImplicitKernel?.Invoke(contract, method);

    /// <summary>Records a legacy caller stamp.</summary>
    public void LegacyStamp(string source)
        => OnLegacyStamp?.Invoke(source);
}
