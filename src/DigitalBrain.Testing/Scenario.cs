using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

public sealed class Scenario : IAsyncDisposable
{
    private readonly ScenarioFaults _faults = new();
    private readonly List<string> _stages = [];
    private int _disposed;

    internal Scenario(OwnerId owner, ScenarioClock clock, IGrainFactory grains)
    {
        Owner = owner;
        Clock = clock;
        Grains = grains;
        RecordStage(ScenarioStages.Open);
    }

    public OwnerId Owner { get; }

    public TimeProvider Clock { get; }

    public IGrainFactory Grains { get; }

    public void AdvanceClock(TimeSpan delta)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ((ScenarioClock)Clock).Advance(delta);
        RecordStage(ScenarioStages.AdvanceClock);
    }

    public FaultHandle Arm(FaultPoint point)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var handle = _faults.Arm(point);
        RecordStage(ScenarioStages.Arm);
        return handle;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        RecordStage(ScenarioStages.Dispose);
        var leftovers = await _faults.DisarmLeftoversAsync();
        if (leftovers.Count == 0)
        {
            return;
        }

        throw CaptureFailure(
            $"Scenario disposed with {leftovers.Count} fault(s) still armed. Dispose each FaultHandle before disposing the Scenario.",
            leftovers);
    }

    internal SimulationAssertionException CaptureFailure(
        string reason,
        ArmedFaultSnapshot? armedFaults = null)
    {
        var snapshot = armedFaults ?? _faults.SnapshotArmed();
        var artifact = new ScenarioFailureArtifact
        {
            Owner = Owner,
            Stages = [.. _stages],
            ArmedFaultCount = snapshot.Count,
            ArmedFaultDescriptions = snapshot.Descriptions,
            ClockUtc = Clock.GetUtcNow(),
            Message = reason,
        };

        return new SimulationAssertionException(reason, artifact);
    }

    private void RecordStage(string stage)
    {
        if (_stages.Count >= ScenarioFailureArtifact.MaxStages)
        {
            return;
        }

        _stages.Add(stage);
    }
}
