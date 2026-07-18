namespace Ino.Core;

/// <summary>
/// Framework-written wrapper around every stored neuron event. Carries causation
/// metadata (caused-by pointers, correlation id, W3C traceparent) so the Playback
/// neuron in Phase 6 can walk the causal graph backward without a central log.
///
/// Authors never construct this directly — the Neuron&lt;TEvent&gt; base
/// class wraps their event in an envelope when RaiseAsync is called, and strips
/// envelopes when GetHistoryAsync returns payloads.
/// </summary>
[GenerateSerializer]
public sealed record EventEnvelope<T>(
    [property: Id(0)] T Payload,
    [property: Id(1)] string EventId,
    [property: Id(2)] string? CausedByEventId,
    [property: Id(3)] string? CausedByStream,
    [property: Id(4)] string CorrelationId,
    [property: Id(5)] DateTimeOffset Timestamp,
    [property: Id(6)] string? TraceParent)
    where T : class, ISynapse;
