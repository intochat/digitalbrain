using System.Text.Json;
using Ino.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Journaling;

namespace Ino.Core.Hosting;

/// <summary>
/// Base class every ino neuron inherits from. Built on Orleans 10's DurableGrain +
/// IDurableList&lt;T&gt; primitive: the base class takes an IDurableList of EventEnvelope&lt;TEvent&gt;
/// in its constructor and exposes a RaiseAsync helper that wraps an event in a causation
/// envelope, appends it to the journal, and persists via WriteStateAsync.
///
/// Authors call:
///   await RaiseAsync(new MyEvent(...), ctx, ct);
/// to append to their journal. The journal itself IS the state — there is no separate
/// projected-state concept in the base class. Authors who want projected state add
/// their own IDurableDictionary&lt;K,V&gt; fields alongside the journal or compute state
/// on demand by enumerating the journal (as the Phase 1 TestNeuron does).
///
/// Persistence is configured at the silo level, not via grain attributes:
///   silo.Services.AddSingleton&lt;IStateMachineStorageProvider, VolatileStateMachineStorageProvider&gt;();
///   silo.AddStateMachineStorage();
/// The Phase 1 InoTestSiloFixture wires the in-memory volatile provider; later phases
/// wire Redis or similar.
/// </summary>
public abstract class Neuron<TEvent>(
    [FromKeyedServices("journal")] IDurableList<EventEnvelope<TEvent>> journal)
    : DurableGrain, IJournaledNeuronQuery, IJournaledNeuronQuery<TEvent>
    where TEvent : class, ISynapse
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Number of events currently in this neuron's journal. Derived neurons can
    /// read this for projections without taking a dependency on the mutable
    /// IDurableList — RaiseAsync remains the only path to append events.
    /// </summary>
    protected int JournalCount => journal.Count;

    /// <summary>
    /// Append a typed event to this neuron's journal. The framework wraps it in an
    /// EventEnvelope&lt;TEvent&gt; carrying causation metadata derived from the supplied
    /// NeuronContext, appends to the journal, then persists via WriteStateAsync.
    /// </summary>
    protected async Task RaiseAsync(
        TEvent @event,
        NeuronContext ctx,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(ctx);

        var envelope = new EventEnvelope<TEvent>(
            Payload: @event,
            EventId: Ulid.NewUlid().ToString(),
            CausedByEventId: ctx.CurrentEventId?.Value,
            CausedByStream: ctx.SourceStream.Value,
            CorrelationId: ctx.CorrelationId.Value,
            Timestamp: DateTimeOffset.UtcNow,
            TraceParent: ctx.CurrentActivity?.Id);

        journal.Add(envelope);
        try
        {
            await WriteStateAsync(ct);
        }
        catch
        {
            // In-memory journal mutation cannot be cleanly rolled back from outside Orleans's
            // grain state machine. Deactivate the activation so the next call reads fresh state
            // from storage and our in-memory view stays consistent with what was actually
            // persisted.
            DeactivateOnIdle();
            throw;
        }
    }

    /// <summary>
    /// Return the last N events from this neuron's journal as typed payloads
    /// (envelope metadata stripped).
    /// </summary>
    public Task<IReadOnlyList<TEvent>> GetHistoryAsync(int lastN = 100)
    {
        if (lastN <= 0) return Task.FromResult<IReadOnlyList<TEvent>>(Array.Empty<TEvent>());

        var skip = Math.Max(0, journal.Count - lastN);
        var list = new List<TEvent>(Math.Min(lastN, journal.Count));
        var index = 0;
        foreach (var env in journal)
        {
            if (index++ < skip) continue;
            list.Add(env.Payload);
        }
        return Task.FromResult<IReadOnlyList<TEvent>>(list);
    }

    /// <summary>
    /// Return the last N events with full envelope metadata. Used by tooling
    /// (Playback, CausationIndex) that needs causation pointers.
    /// </summary>
    public Task<IReadOnlyList<EventEnvelope<TEvent>>> GetHistoryWithMetadataAsync(int lastN = 100)
    {
        if (lastN <= 0) return Task.FromResult<IReadOnlyList<EventEnvelope<TEvent>>>(Array.Empty<EventEnvelope<TEvent>>());

        var skip = Math.Max(0, journal.Count - lastN);
        var list = new List<EventEnvelope<TEvent>>(Math.Min(lastN, journal.Count));
        var index = 0;
        foreach (var env in journal)
        {
            if (index++ < skip) continue;
            list.Add(env);
        }
        return Task.FromResult<IReadOnlyList<EventEnvelope<TEvent>>>(list);
    }

    /// <summary>
    /// Non-generic journal lookup used by the Phase 6 Playback neuron. Scans the
    /// journal for an entry matching the supplied event id and returns a
    /// type-erased view of its metadata + JSON-serialized payload.
    /// </summary>
    public Task<JournaledEventInfo?> FindEventAsync(string eventId)
    {
        if (string.IsNullOrEmpty(eventId)) return Task.FromResult<JournaledEventInfo?>(null);

        foreach (var env in journal)
        {
            if (env.EventId == eventId)
            {
                var payloadTypeName = env.Payload.GetType().FullName ?? env.Payload.GetType().Name;
                var payloadJson = JsonSerializer.Serialize(env.Payload, env.Payload.GetType(), JsonOptions);
                return Task.FromResult<JournaledEventInfo?>(new JournaledEventInfo(
                    EventId: env.EventId,
                    PayloadTypeName: payloadTypeName,
                    PayloadJson: payloadJson,
                    CausedByEventId: env.CausedByEventId,
                    CausedByStream: env.CausedByStream,
                    CorrelationId: env.CorrelationId,
                    Timestamp: env.Timestamp,
                    TraceParent: env.TraceParent));
            }
        }
        return Task.FromResult<JournaledEventInfo?>(null);
    }
}
