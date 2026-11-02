namespace DigitalBrain;

/// <summary>Canonical base record for neuron persistent state holding the subscriber's signal cursor.</summary>
public record NeuronState
{
    /// <summary>The highest monotonic position read in the unified brain signal log.</summary>
    public long SignalCursor { get; init; }

    /// <summary>Dead-letter entries for signals that failed reaction handling after retry exhaustion.</summary>
    public DeadLetterEntry[] DeadLetters { get; init; } = [];
}

/// <summary>An unhandled poison signal recorded in subscriber state.</summary>
public sealed record DeadLetterEntry(
    long Position,
    string Publisher,
    string SignalType,
    string ErrorMessage,
    DateTimeOffset At);
