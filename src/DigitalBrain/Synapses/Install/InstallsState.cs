namespace DigitalBrain;

/// <summary>The state of the installs neuron: one record per installed alias and the asks waiting for the person.</summary>
/// <param name="Records">The installed apps.</param>
/// <param name="Pending">The installs asked and not yet resolved; at most one per alias.</param>
public sealed record InstallsState(IReadOnlyList<InstallRecord> Records, IReadOnlyList<PendingInstall> Pending)
{
    /// <summary>No installs.</summary>
    public InstallsState() : this([], []) { }
}
