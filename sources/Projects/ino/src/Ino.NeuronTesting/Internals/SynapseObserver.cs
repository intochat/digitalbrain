using System.Collections.Concurrent;
using System.Diagnostics;
using Ino.Core;
using Ino.Core.Hosting;

namespace Ino.NeuronTesting.Internals;

// Captures synapse activities matching one correlation_id.
// Production tag schema (verified from neuron sources):
//   ino.synapse.type     = synapse class name (e.g. "PlanTripRequest")
//   ino.correlation_id   = CorrelationId.Value string
//   optional ino.synapse.arg.<key> per-arg pairs
public sealed class SynapseObserver : IDisposable
{
    readonly string _correlationId;
    readonly ConcurrentBag<SynapseFire> _captured = [];
    readonly ActivityListener _listener;

    public SynapseObserver(string correlationId)
    {
        _correlationId = correlationId;
        _listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == Telemetry.ActivitySourceName,
            // Both Sample callbacks must be set: Sample handles ActivityContext parents,
            // SampleUsingParentId handles string-based (legacy W3C) parents.
            Sample            = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _)       => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = OnStopped,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyList<SynapseFire> Observed => _captured.ToArray();

    // Captures both the legacy "ino.neuron.handle" literal used by today's
    // domain neurons AND the canonical Telemetry.Spans.Handle/Fire/FireBroadcast
    // formats. Either source emits the ino.synapse.type + ino.correlation_id
    // tags the observer reads.
    static bool IsSynapseActivity(string opName) =>
        opName == "ino.neuron.handle"
        || opName.StartsWith("handle ", StringComparison.Ordinal)
        || opName.StartsWith("fire ", StringComparison.Ordinal)
        || opName.StartsWith("fire-broadcast ", StringComparison.Ordinal);

    void OnStopped(Activity activity)
    {
        if (!IsSynapseActivity(activity.OperationName)) return;
        var corr = activity.GetTagItem(Telemetry.Tags.CorrelationId) as string;
        if (corr != _correlationId) return;
        var type = activity.GetTagItem(Telemetry.Tags.SynapseType) as string ?? "(unknown)";
        // Activity.Tags is IEnumerable<KeyValuePair<string, string?>> — cast Value safely
        var args = activity.Tags
            .Where(kv => kv.Key.StartsWith(Telemetry.Tags.SynapseArgPrefix, StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key[Telemetry.Tags.SynapseArgPrefix.Length..], kv => kv.Value ?? "");
        _captured.Add(new SynapseFire(type, new CorrelationId(corr), args, new DateTimeOffset(activity.StartTimeUtc, TimeSpan.Zero)));
    }

    public void Dispose() => _listener.Dispose();
}
