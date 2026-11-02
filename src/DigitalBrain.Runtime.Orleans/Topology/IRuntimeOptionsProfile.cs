namespace DigitalBrain.Runtime.Orleans;

/// <summary>
/// A topology profile that names its configuration keys and stores through <see cref="RuntimeOptions"/>.
/// <see cref="TopologyHosting.UseTopology"/> refuses a profile whose options differ from the ones the host registered.
/// </summary>
public interface IRuntimeOptionsProfile
{
    /// <summary>The options this profile was built with.</summary>
    RuntimeOptions Options { get; }
}
