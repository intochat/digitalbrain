using DigitalBrain.Hosting;

namespace DigitalBrain.Testing.Conformance.Host;

/// <summary>What a fact asks of a host: retention for checkpoints and duplicate delivery. Every target maps it onto its own knobs.</summary>
public sealed class FrameworkHostOptions
{
    /// <summary>Signals per publisher a checkpoint keeps at least; null keeps the target default.</summary>
    public int? HistoryWindowSignals { get; init; }

    /// <summary>Signals younger than this survive a checkpoint; null keeps the target default.</summary>
    public TimeSpan? HistoryWindowAge { get; init; }

    /// <summary>A cursor not advanced for this long stops pinning the floor; null keeps the target default.</summary>
    public TimeSpan? StaleCursorAfter { get; init; }

    /// <summary>Every signal reference is delivered twice.</summary>
    public bool DuplicateDelivery { get; init; }
}

/// <summary>A place the host-conformance facts run (03-09, 04-15): TestOS, the local topology, or a consumer's own host.</summary>
public abstract class FrameworkTarget
{
    public abstract string Name { get; }

    /// <summary>Starts a host with the shared controls.</summary>
    public abstract Task<RunningTarget> StartHost(FrameworkHostOptions options, CancellationToken ct);

    public override string ToString() => Name;
}

/// <summary>A target that runs an Orleans silo in-process through the Phase 3 seam.</summary>
public abstract class OrleansFrameworkTarget : FrameworkTarget
{
    /// <summary>Starts a plain in-process host (03-09).</summary>
    public abstract Task<IInProcessHost> Start(CancellationToken ct);
}

/// <summary>A running host and the controls every target offers the same way.</summary>
public abstract class RunningTarget : IAsyncDisposable
{
    /// <summary>A typed proxy to <paramref name="id"/>. Take a fresh one after <see cref="Restart"/>.</summary>
    public abstract T Get<T>(NeuronId id) where T : class;

    /// <summary>Ends the activation of <paramref name="id"/>; state, synapses and reminders survive.</summary>
    public abstract Task Deactivate(NeuronId id);

    /// <summary>Stops and starts the host over the same data.</summary>
    public abstract Task Restart(CancellationToken ct);

    /// <summary>Makes exactly the next journal append of <paramref name="publisher"/> publish no reference and the saving call throw: a kill between write and publish.</summary>
    public abstract void FailNextPublishAfterAppend(NeuronId publisher);

    /// <summary>Moves the host clock forward.</summary>
    public abstract void Advance(TimeSpan by);

    /// <summary>Runs a checkpoint of <paramref name="brain"/>'s journal now.</summary>
    public abstract Task ForceCheckpoint(BrainId brain, CancellationToken ct);

    /// <summary>Completes when queued work is done, where the target can tell; facts still poll with a deadline.</summary>
    public abstract Task Idle();

    /// <summary>How many live subscriptions <paramref name="subscriber"/> holds for signals of <paramref name="signal"/> from <paramref name="source"/>.</summary>
    public abstract Task<int> SubscriptionCount(NeuronId subscriber, NeuronId source, Type signal);

    public abstract ValueTask DisposeAsync();
}
