namespace DigitalBrain;

/// <summary>A signal as journaled: its publisher, its sequence and when it was published.</summary>
public sealed record JournaledSignal(NeuronId Publisher, long Sequence, Signal Signal, DateTimeOffset At);

/// <summary>A page of signal history.</summary>
/// <param name="Floor">The lowest sequence still held; earlier entries were trimmed.</param>
/// <param name="Last">The highest sequence written so far.</param>
/// <param name="Signals">The entries of the page, ordered by sequence.</param>
public sealed record SignalPage(long Floor, long Last, IReadOnlyList<JournaledSignal> Signals)
{
    /// <summary>The largest page a reader may ask for.</summary>
    public const int MaxLimit = 1000;

    /// <summary>Throws unless <paramref name="limit"/> is within 1..<see cref="MaxLimit"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The limit is out of range.</exception>
    public static int CheckLimit(int limit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MaxLimit);
        return limit;
    }
}
