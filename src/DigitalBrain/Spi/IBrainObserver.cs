namespace DigitalBrain.Spi;

/// <summary>
/// The kernel's read-only facts about the caller's brain: which neurons exist, which types are compiled, which brains the caller is in,
/// which synapses are held, and the signals delivered along them from now on. The brain is the stamped caller's.
/// A runtime with no stamped caller lists every brain it holds.
/// </summary>
public interface IBrainObserver
{
    /// <summary>Instances of one neuron contract in the caller's brain.</summary>
    IAsyncEnumerable<BrainInstance> Instances<T>(CancellationToken cancellationToken) where T : class;

    /// <summary>Instances of one neuron type, or of every type when <paramref name="type"/> is null.</summary>
    IAsyncEnumerable<BrainInstance> Instances(string? type, CancellationToken cancellationToken = default);

    /// <summary>The compiled modules' neurons, signals and connectors, then the brain's own features.</summary>
    Task<BrainType[]> Types(CancellationToken cancellationToken = default);

    /// <summary>The brains the stamped caller is a member of, the current one included.</summary>
    Task<string[]> Brains(CancellationToken cancellationToken = default);

    /// <summary>Synapses of the caller's brain: one row per source, signal alias and subscriber. A static graph.</summary>
    Task<ObservedSynapse[]> Synapses(CancellationToken cancellationToken = default);

    /// <summary>Signal deliveries in the caller's brain from now on. Nothing already delivered is replayed.</summary>
    IAsyncEnumerable<SignalDelivery> Deliveries(CancellationToken cancellationToken = default);
}
