namespace DigitalBrain;

/// <summary>
/// Distinguishes a host-authenticated external input from a fact authored by a behavior.
/// </summary>
public enum SynapseOriginAuthority
{
    /// <summary>
    /// A record written before origin authority was added. Hosting resolves it from
    /// its trusted source identity before delivering it to a behavior.
    /// </summary>
    LegacyUnknown,

    /// <summary>
    /// The source is a registered behavior or Hosting-owned runtime actor.
    /// </summary>
    Internal,

    /// <summary>
    /// The source is a host-authenticated external ingress channel.
    /// </summary>
    ExternalIngress,
}

public sealed record SynapseOrigin(
    NeuronId Source,
    long Sequence,
    DateTimeOffset OccurredAt,
    SynapseOriginAuthority Authority = SynapseOriginAuthority.LegacyUnknown)
{
    /// <summary>
    /// True only when Hosting stamped the delivered fact as an authenticated external input.
    /// </summary>
    public bool IsExternalIngress => Authority == SynapseOriginAuthority.ExternalIngress;
}
