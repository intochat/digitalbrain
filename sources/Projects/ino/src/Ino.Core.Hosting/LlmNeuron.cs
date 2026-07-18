using System.Text.Json;
using Core.Contracts;
using IAW.Core;
using Ino.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Journaling;

namespace Ino.Core.Hosting;

/// <summary>
/// LLM-backed neuron base class. Inherits IAW's <see cref="Agent"/> for
/// IChatClient streaming, tool registration, durable chat history, and the
/// tool-approval middleware, then layers the journal-event API of
/// <see cref="Neuron{TEvent}"/> on top.
///
/// Use this when a neuron needs to reason. For pure-code neurons that never
/// touch an LLM, keep using <see cref="Neuron{TEvent}"/> — the LLM-optional
/// contract from `docs/product-vision-final.md` is preserved.
///
/// Constructor takes the same <c>[AgentState] AgentDurableState</c> + IChatClient
/// pair as Agent, plus the keyed journal that Neuron uses. Persistence wiring
/// is the same (Orleans 10 IStateMachineStorageProvider + AddStateMachineStorage)
/// — see <see cref="InoJournalingExtensions.UseInoJournaling"/>.
/// </summary>
public abstract class LlmNeuron<TEvent>(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient,
    [FromKeyedServices("journal")] IDurableList<EventEnvelope<TEvent>> journal)
    : Agent(durableState, chatClient), IJournaledNeuronQuery, IJournaledNeuronQuery<TEvent>
    where TEvent : class, ISynapse
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    protected int JournalCount => journal.Count;

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
            DeactivateOnIdle();
            throw;
        }
    }

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
