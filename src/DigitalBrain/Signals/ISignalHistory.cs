namespace DigitalBrain.Spi;

/// <summary>
/// The brain's journal of published signals, read in sequence order. Entries below the floor have been trimmed;
/// a subscriber that fell below it receives a <see cref="Gap"/> instead of the missing signals.
/// </summary>
public interface ISignalHistory
{
    /// <summary>Signals <paramref name="publisher"/> published after <paramref name="since"/>, at most <paramref name="limit"/>, by sequence.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is outside 1..1000.</exception>
    Task<SignalPage> Read(NeuronId publisher, long since, int limit, CancellationToken ct);

    /// <summary>Signals of the whole brain after <paramref name="since"/>, at most <paramref name="limit"/>, by sequence.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is outside 1..1000.</exception>
    Task<SignalPage> Read(long since, int limit, CancellationToken ct);
}
