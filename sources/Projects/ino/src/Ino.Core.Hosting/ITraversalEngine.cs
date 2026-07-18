using Ino.Core;

namespace Ino.Core.Hosting;

/// <summary>
/// In-process helper that lets an <see cref="INeuronPlan"/> walk the neuron
/// graph during a single execution. Three primitives mirror the BFS shape:
/// <list type="bullet">
///   <item><description><b>Visit</b> a neuron — read its journal as the BFS node-read.</description></item>
///   <item><description><b>Fire</b> a synapse — traverse an outgoing edge.</description></item>
///   <item><description><b>Reason</b> — optional LLM hop for fuzzy decisions inside the plan.</description></item>
/// </list>
/// The seven traversal primitives in the design (frequency, negative-search,
/// temporal-window, recurrence, cloning, co-occurrence, content-scan) are all
/// expressible as <see cref="RecallQuery{TEvent}"/> shapes over <see cref="VisitAsync"/>.
///
/// Engines are per-call: a plan constructs one internally in
/// <see cref="INeuronPlan.ExecuteAsync"/> from its DI services and the
/// inbound <see cref="NeuronPlanContext"/>. Stateless across calls.
/// </summary>
public interface ITraversalEngine
{
    /// <summary>
    /// Visit a journaled neuron — read its event log filtered by <paramref name="query"/>.
    /// Resolves the closed generic <see cref="IJournaledNeuronQuery{TEvent}"/> via
    /// <see cref="Orleans.IGrainFactory"/>; one grain per <typeparamref name="TEvent"/>
    /// in v0.1 means resolution is unambiguous.
    /// </summary>
    Task<IReadOnlyList<EventEnvelope<TEvent>>> VisitAsync<TEvent>(
        string primaryKey,
        RecallQuery<TEvent> query,
        CancellationToken ct = default)
        where TEvent : class, ISynapse;

    /// <summary>
    /// Fire a synapse — same semantics as <see cref="IFirePort.Fire{T}"/>, but the
    /// <see cref="NeuronContext"/> bound to this engine's construction is reused
    /// so plan code doesn't have to thread it through every call.
    /// </summary>
    Task<NeuronResult> FireAsync<T>(T synapse, CancellationToken ct = default)
        where T : ISynapse;

    /// <summary>
    /// Optional LLM hop — used by plans that need natural-language reasoning over
    /// intermediate journal contents (e.g., topic-extraction in the gift-suggestion
    /// scenario). Throws <see cref="NotSupportedException"/> when no chat client is
    /// configured for the plan's silo.
    /// </summary>
    Task<string> ReasonAsync(
        string instruction,
        object? context = null,
        CancellationToken ct = default);
}
