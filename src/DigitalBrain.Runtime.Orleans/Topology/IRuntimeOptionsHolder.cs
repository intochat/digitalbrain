namespace DigitalBrain.Runtime.Orleans;

/// <summary>
/// A topology that names its configuration keys and stores through <see cref="RuntimeOptions"/>.
/// <see cref="TopologyHosting.UseTopology"/> refuses a topology whose options differ from the ones the host registered.
/// </summary>
public interface IRuntimeOptionsHolder
{
    /// <summary>The options this topology was built with.</summary>
    RuntimeOptions Options { get; }
}
