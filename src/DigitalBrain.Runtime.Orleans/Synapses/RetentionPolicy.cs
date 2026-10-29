using Microsoft.Extensions.Options;

namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>The floor never passes a live cursor; history keeps the larger of the age and signal windows.</summary>
internal sealed class RetentionPolicy(IOptions<RetentionOptions> options, TimeProvider time, ISynapseCursorLookup cursors) : IRetentionPolicy
{
    public TimeProvider Time { get; } = time;

    public async ValueTask<long> FloorFor(string brainKey, NeuronId publisher, long last, Func<TimeSpan, long> countNewerThan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(countNewerThan);
        ct.ThrowIfCancellationRequested();
        var lowest = await cursors.LowestLive(brainKey, publisher).ConfigureAwait(false);
        return Floor(options.Value, last, countNewerThan, lowest);
    }

    internal static long Floor(RetentionOptions o, long last, Func<TimeSpan, long> countNewerThan, long? lowestLive)
    {
        var window = Math.Max(o.HistoryWindowSignals, countNewerThan(o.HistoryWindowAge));
        return Math.Max(0, Math.Min(lowestLive ?? long.MaxValue, last - window));
    }
}

/// <summary>The fallback when synapses are not registered: no live cursors, only the history window.</summary>
internal sealed class DefaultRetentionPolicy(IOptions<RetentionOptions> options) : IRetentionPolicy
{
    public ValueTask<long> FloorFor(string brainKey, NeuronId publisher, long last, Func<TimeSpan, long> countNewerThan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(countNewerThan);
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(RetentionPolicy.Floor(options.Value, last, countNewerThan, null));
    }
}
