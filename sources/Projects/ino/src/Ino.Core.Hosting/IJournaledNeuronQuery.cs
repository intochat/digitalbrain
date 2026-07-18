using Ino.Core;
using Orleans;

namespace Ino.Core.Hosting;

/// <summary>
/// Non-generic grain interface implemented by every Neuron&lt;TEvent&gt; via the base class.
/// The Phase 6 Playback neuron uses this to walk a neuron's journal backward without
/// knowing the neuron's concrete TEvent type at compile time.
///
/// The return type is deliberately object-typed (string for event id, object for payload)
/// because the caller is doing graph traversal, not typed dispatch — it needs metadata,
/// not the typed payload itself.
/// </summary>
public interface IJournaledNeuronQuery : IGrainWithStringKey
{
    /// <summary>
    /// Find a specific event in this neuron's journal by event id. Returns null if the
    /// event is not present. The returned object carries the envelope's metadata fields
    /// (EventId, CausedByEventId, CausedByStream, CorrelationId, Timestamp, TraceParent)
    /// plus a string representation of the payload type.
    /// </summary>
    Task<JournaledEventInfo?> FindEventAsync(string eventId);
}

/// <summary>
/// Typed journal-query grain interface. Implemented automatically by every
/// <see cref="Neuron{TEvent}"/> via the base class so cross-silo callers
/// (Cortex BFS plans, Playback) can read a neuron's event log without knowing
/// the concrete grain implementation type at compile time. Grain resolution
/// works because v0.1 has one grain per event type — Orleans matches the
/// closed-generic interface unambiguously. When a second grain shares an event
/// type post-v0.1, plumb explicit <c>[GrainType]</c> aliases through the
/// traversal API.
/// </summary>
public interface IJournaledNeuronQuery<TEvent> : IGrainWithStringKey
    where TEvent : class, ISynapse
{
    Task<IReadOnlyList<TEvent>> GetHistoryAsync(int lastN = 100);

    Task<IReadOnlyList<EventEnvelope<TEvent>>> GetHistoryWithMetadataAsync(int lastN = 100);
}

/// <summary>
/// Non-generic view of an EventEnvelope&lt;T&gt; returned from IJournaledNeuronQuery.FindEventAsync.
/// Carries all metadata fields but not the typed payload (payload is represented as its
/// type name + a JSON-serialized string for debugging).
/// </summary>
[GenerateSerializer]
public sealed record JournaledEventInfo(
    [property: Id(0)] string EventId,
    [property: Id(1)] string PayloadTypeName,
    [property: Id(2)] string PayloadJson,
    [property: Id(3)] string? CausedByEventId,
    [property: Id(4)] string? CausedByStream,
    [property: Id(5)] string CorrelationId,
    [property: Id(6)] DateTimeOffset Timestamp,
    [property: Id(7)] string? TraceParent);
