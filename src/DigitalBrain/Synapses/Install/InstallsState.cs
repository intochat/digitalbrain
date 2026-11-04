namespace DigitalBrain;

/// <summary>The state of the installs neuron: one record per installed alias.</summary>
/// <param name="Records">The installed apps.</param>
public sealed record InstallsState(IReadOnlyList<InstallRecord> Records)
{
    /// <summary>No installs.</summary>
    public InstallsState() : this([]) { }
}
