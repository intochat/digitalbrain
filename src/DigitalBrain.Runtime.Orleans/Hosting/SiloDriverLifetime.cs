using DigitalBrain.Operations;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Hosting;

/// <summary>Captures and validates silo driver lifetime evidence using Orleans cluster membership.</summary>
public sealed class SiloDriverLifetime(ILocalSiloDetails siloDetails, IClusterMembershipService membership) : IOperationDriverLifetime
{
    private readonly ILocalSiloDetails _siloDetails = siloDetails ?? throw new ArgumentNullException(nameof(siloDetails));
    private readonly IClusterMembershipService _membership = membership ?? throw new ArgumentNullException(nameof(membership));

    /// <inheritdoc />
    public string? Capture() => _siloDetails.SiloAddress.ToParsableString();

    /// <inheritdoc />
    public bool? HasEnded(string token) => DriverHasEnded(token, _membership.CurrentSnapshot);

    internal static bool DriverHasEnded(string driver, ClusterMembershipSnapshot snapshot)
    {
        if (!SiloAddress.TryParse(driver, out var address))
        {
            return false;
        }

        if (snapshot.Members.TryGetValue(address, out var member))
        {
            return member.Status == SiloStatus.Dead;
        }

        return snapshot.Members.Values.Any(current => current.Status == SiloStatus.Active && current.SiloAddress.IsSuccessorOf(address));
    }
}
