using Orleans;
using Orleans.Concurrency;

namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>One per brain (keyed by brain key): the synapse cursors on the brain's publishers (04 D-04).</summary>
[Alias("brain.synapse-cursors")]
internal interface ISynapseCursorRegistry : IGrainWithStringKey
{
    /// <summary>Records the subscriber's cursor on the publisher at the current time.</summary>
    Task ReportCursor(string subscriber, NeuronId publisher, long cursor);

    /// <summary>The lowest cursor reported within StaleCursorAfter; null when none.</summary>
    [ReadOnly, AlwaysInterleave]
    Task<long?> LowestLive(NeuronId publisher);

    /// <summary>Removes every cursor of the subscriber.</summary>
    Task Forget(string subscriber);

    /// <summary>Subscribers whose live cursor on the publisher is below <paramref name="last"/>.</summary>
    [ReadOnly]
    Task<string[]> Behind(NeuronId publisher, long last);

    /// <summary>Publishers with at least one cursor.</summary>
    [ReadOnly]
    Task<NeuronId[]> Publishers();
}
