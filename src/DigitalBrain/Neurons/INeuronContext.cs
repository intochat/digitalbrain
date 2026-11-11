using DigitalBrain.Access;
using DigitalBrain.Kernel;

namespace DigitalBrain.Neurons;

/// <summary>
/// What a runtime gives a neuron, attached once before activation: who it is, who is calling, the clock,
/// its timers, its subscriptions, its calls and its publishes.
/// </summary>
public interface INeuronContext
{
    /// <summary>The neuron's id.</summary>
    NeuronId Id { get; }

    /// <summary>The caller behind the currently running turn, or null when the host stamps none.</summary>
    Caller? Caller { get; }

    /// <summary>The clock.</summary>
    TimeProvider Time { get; }

    /// <summary>Timers that live with the activation.</summary>
    ITimers Timers { get; }

    /// <summary>Durable reminders that reactivate the neuron.</summary>
    IReminders Reminders { get; }

    /// <summary>The synapses this neuron holds as a subscriber.</summary>
    ISynapses Synapses { get; }

    /// <summary>The trace id a save made now would be journaled with; null when the context does not stamp traces.</summary>
    string? TraceId => null;

    /// <summary>Publishes a signal from this neuron.</summary>
    Task Publish(Signal signal);

    /// <summary>Publishes an ephemeral signal from this neuron without persisting to the journal.</summary>
    Task PublishEphemeral(Signal signal) => Publish(signal);

    /// <summary>A typed interface to another neuron of the same brain.</summary>
    T Get<T>(NeuronId id) where T : class;

    /// <summary>Runs <paramref name="work"/> after the current turn ends.</summary>
    void Defer(Func<Task> work);

    /// <summary>Tests whether <paramref name="error"/> represents an interruption caused by host shutdown or silo unavailability.</summary>
    bool Interrupts(Exception error) => false;

    /// <summary>Detaches background work outside the current turn.</summary>
    void Detach(Func<CancellationToken, Task> work) { _ = work(CancellationToken.None); }
}
