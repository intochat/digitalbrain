using Orleans.Runtime;

namespace DigitalBrain.Testing;

internal sealed class ScenarioFaults
{
    private readonly object _gate = new();
    private readonly HashSet<FaultHandle> _armed = [];

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

                lock (_gate)
                {
                    _armed.Add(handle);
                }

                return handle;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(point),
                    point.GetType(),
                    "Fault point is not in the closed durability catalog.");
        }
    }

    public async ValueTask DisarmLeftoversAndThrowIfAnyAsync()
    {
        FaultHandle[] leftovers;
        lock (_gate)
        {
            leftovers = [.. _armed];
        }

        foreach (var handle in leftovers)
        {
            await handle.DisposeAsync();
        }

        if (leftovers.Length > 0)
        {
            throw new SimulationAssertionException(
                $"Scenario disposed with {leftovers.Length} fault(s) still armed. Dispose each FaultHandle before disposing the Scenario.");
        }
    }

    private void Disarm(FaultHandle handle, GrainId grain)
    {
        SimulationClusterHost.ClearJournalWriteFailure(grain);

        lock (_gate)
        {
            _armed.Remove(handle);
        }
    }
}
