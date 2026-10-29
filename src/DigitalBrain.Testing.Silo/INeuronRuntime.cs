using DigitalBrain;

namespace DigitalBrain.Testing;

// One brain on one runtime kind, as a fact body sees it.
public interface INeuronRuntime : IAsyncDisposable
{
    NeuronRuntimeKind Kind { get; }

    BrainId Brain { get; }

    IServiceProvider Services { get; }

    // The neuron id of type and key in this runtime's brain.
    NeuronId Id(string type, string key) => new(Brain, type, key);

    T Get<T>(NeuronId id) where T : class;

    // Lets queued turns, deliveries and ticks finish.
    Task Idle();

    // Lets time pass: TestOS advances its clock; the silo waits for real.
    Task Elapse(TimeSpan by, CancellationToken ct);

    // Every held signal of type T that publisher journaled, oldest first.
    Task<IReadOnlyList<T>> Signals<T>(NeuronId publisher, CancellationToken ct) where T : Signal;

    // Waits until publisher has journaled at least count signals of type T.
    async Task<IReadOnlyList<T>> WaitForSignals<T>(NeuronId publisher, int count, TimeSpan within, CancellationToken ct) where T : Signal
    {
        var deadline = DateTime.UtcNow + within;
        while (true)
        {
            await Idle().ConfigureAwait(false);
            var signals = await Signals<T>(publisher, ct).ConfigureAwait(false);
            if (signals.Count >= count) { return signals; }
            if (DateTime.UtcNow > deadline) { throw new TimeoutException($"{publisher} journaled {signals.Count} {typeof(T).Name}, not {count}, within {within}."); }
            await Elapse(TimeSpan.FromMilliseconds(50), ct).ConfigureAwait(false);
        }
    }

    // Stops every activation and starts again over the same state (TestOS keeps its journal; the silo keeps its home).
    Task Restart(CancellationToken ct);
}
