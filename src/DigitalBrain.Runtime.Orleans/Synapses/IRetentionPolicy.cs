namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>Computes the retention floor of a publisher's history (04 D-04, D-11).</summary>
public interface IRetentionPolicy
{
    /// <summary>
    /// The lowest sequence still kept: <c>max(0, min(lowest live cursor, last - max(HistoryWindowSignals, countNewerThan(HistoryWindowAge))))</c>.
    /// </summary>
    /// <param name="brainKey">The brain that owns the publisher.</param>
    /// <param name="publisher">The publishing neuron.</param>
    /// <param name="last">The publisher's last sequence.</param>
    /// <param name="countNewerThan">Counts entries younger than the given age; entries with unknown time count as younger.</param>
    /// <param name="ct">Cancellation.</param>
    ValueTask<long> FloorFor(string brainKey, NeuronId publisher, long last, Func<TimeSpan, long> countNewerThan, CancellationToken ct);
}

/// <summary>Answers the lowest live synapse cursor on a publisher.</summary>
internal interface ISynapseCursorLookup
{
    ValueTask<long?> LowestLive(string brainKey, NeuronId publisher);
}
