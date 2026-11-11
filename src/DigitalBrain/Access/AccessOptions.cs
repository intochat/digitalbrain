namespace DigitalBrain.Access;

/// <summary>
/// Operational access gate configuration.
/// </summary>
public sealed class AccessOptions
{
    /// <summary>
    /// When true, non-visible contracts and contracts marked [Internal] without a stamped caller
    /// are audited and admitted as Kernel rather than rejected, preserving legacy behavior during rollout.
    /// </summary>
    public bool AuditContractInternal { get; set; } = true;

    /// <summary>
    /// When true (e.g. in Release 1 and TestOS), unstamped edge calls default to Caller.OfKernel rather than throwing.
    /// </summary>
    public bool UnstampedEdgeIsKernel { get; set; } = true;
}
