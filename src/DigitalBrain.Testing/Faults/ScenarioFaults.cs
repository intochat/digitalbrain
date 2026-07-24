using Orleans.Runtime;

namespace DigitalBrain.Testing;

internal readonly record struct ArmedFaultSnapshot(int Count, IReadOnlyList<string> Descriptions);

internal sealed class ScenarioFaults
{
    private readonly object _gate = new();
    private readonly Dictionary<FaultHandle, string> _armed = [];

    public FaultHandle Arm(FaultPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);

        switch (point)
        {
            case JournalCommitAfter journal:
                ArgumentOutOfRangeException.ThrowIfNegative(journal.CompletedWritesBeforeFailure);
                ArgumentException.ThrowIfNullOrWhiteSpace(journal.Message);

                SimulationClusterHost.FailJournalWriteAfter(
                    journal.Grain,
                    journal.CompletedWritesBeforeFailure,
                    journal.Message);

                FaultHandle? handle = null;
                handle = new FaultHandle(() => Disarm(handle!, journal.Grain));

                var description = BoundDescription(
                    $"{nameof(JournalCommitAfter)} grain={journal.Grain} after={journal.CompletedWritesBeforeFailure} message={journal.Message}");

                lock (_gate)
                {
                    _armed[handle] = description;
                }

                return handle;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(point),
                    point.GetType(),
                    "Fault point is not in the closed durability catalog.");
        }
    }

    public async ValueTask<ArmedFaultSnapshot> DisarmLeftoversAsync()
    {
        FaultHandle[] leftovers;
        ArmedFaultSnapshot snapshot;
        lock (_gate)
        {
            leftovers = [.. _armed.Keys];
            snapshot = SnapshotUnlocked();
        }

        foreach (var handle in leftovers)
        {
            await handle.DisposeAsync();
        }

        return snapshot;
    }

    public ArmedFaultSnapshot SnapshotArmed()
    {
        lock (_gate)
        {
            return SnapshotUnlocked();
        }
    }

    private ArmedFaultSnapshot SnapshotUnlocked()
        => new(
            Count: _armed.Count,
            Descriptions: [.. _armed.Values.Take(ScenarioFailureArtifact.MaxFaultDescriptions)]);

    private void Disarm(FaultHandle handle, GrainId grain)
    {
        SimulationClusterHost.ClearJournalWriteFailure(grain);

        lock (_gate)
        {
            _armed.Remove(handle);
        }
    }

    private static string BoundDescription(string description)
    {
        if (description.Length <= ScenarioFailureArtifact.MaxFaultDescriptionLength)
        {
            return description;
        }

        return description[..ScenarioFailureArtifact.MaxFaultDescriptionLength];
    }
}
