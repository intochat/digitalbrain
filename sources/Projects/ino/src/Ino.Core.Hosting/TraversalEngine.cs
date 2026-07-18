using System.Text.Json;
using Ino.Core;
using Microsoft.Extensions.AI;
using Orleans;

namespace Ino.Core.Hosting;

/// <summary>
/// Default <see cref="ITraversalEngine"/> implementation. Wraps the live
/// per-call <see cref="NeuronContext"/> and the silo's grain factory + fire
/// port + chat client. Plans construct one internally per execution; not
/// registered as a singleton because <see cref="NeuronContext"/> is per-call.
/// </summary>
public sealed class TraversalEngine(
    IGrainFactory grains,
    IFirePort firePort,
    NeuronContext context,
    IChatClient? chatClient = null) : ITraversalEngine
{
    public async Task<IReadOnlyList<EventEnvelope<TEvent>>> VisitAsync<TEvent>(
        string primaryKey,
        RecallQuery<TEvent> query,
        CancellationToken ct = default)
        where TEvent : class, ISynapse
    {
        ArgumentException.ThrowIfNullOrEmpty(primaryKey);
        ArgumentNullException.ThrowIfNull(query);

        var grain = grains.GetGrain<IJournaledNeuronQuery<TEvent>>(primaryKey);

        // LastN bounds the cross-silo payload size. Predicate filters run after
        // because Func<,> can't cross the wire — that's fine for v0.1 journal
        // sizes; push predicates server-side when they don't fit.
        var lastN = query.LastN ?? 1000;
        var history = await grain.GetHistoryWithMetadataAsync(lastN);

        if (query.Since is null && query.Until is null && query.Where is null && query.WhereEnvelope is null)
            return history;

        var filtered = new List<EventEnvelope<TEvent>>(history.Count);
        foreach (var env in history)
        {
            if (query.Since is { } since && env.Timestamp < since) continue;
            if (query.Until is { } until && env.Timestamp > until) continue;
            if (query.WhereEnvelope is { } pe && !pe(env)) continue;
            if (query.Where is { } pp && !pp(env.Payload)) continue;
            filtered.Add(env);
        }
        return filtered;
    }

    public Task<NeuronResult> FireAsync<T>(T synapse, CancellationToken ct = default)
        where T : ISynapse =>
        firePort.Fire(synapse, context, ct);

    public async Task<string> ReasonAsync(
        string instruction,
        object? context = null,
        CancellationToken ct = default)
    {
        if (chatClient is null)
            throw new NotSupportedException(
                "TraversalEngine.ReasonAsync requires an IChatClient registered on this silo's DI.");

        ArgumentException.ThrowIfNullOrEmpty(instruction);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, instruction),
        };
        if (context is not null)
        {
            var json = JsonSerializer.Serialize(context, context.GetType());
            messages.Add(new ChatMessage(ChatRole.User, json));
        }

        var response = await chatClient.GetResponseAsync(messages, options: null, ct);
        return response.Text ?? string.Empty;
    }
}
