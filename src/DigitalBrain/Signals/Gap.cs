namespace DigitalBrain;

/// <summary>
/// Delivered to a subscriber that fell below the history floor of <paramref name="Source"/>: the signals with sequences
/// <paramref name="From"/> through <paramref name="To"/> are gone. The subscriber rebuilds from the source's current state.
/// </summary>
[Signal("synapse.gap")]
public sealed record Gap(NeuronId Source, long From, long To) : Signal;
