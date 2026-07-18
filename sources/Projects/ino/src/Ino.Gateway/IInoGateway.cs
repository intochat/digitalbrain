using Ino.Core;
using Ino.Core.Hosting;
using Ino.Kernel.Contracts;

namespace Ino.Gateway;

/// <summary>
/// Single interface behind every LLM-driving transport (gRPC for Flutter,
/// MCP for Claude Code / external LLMs, CLI for dev). Phase 3 source generator
/// can project this interface into all three transport surfaces without drift.
/// Slice 1 ships <see cref="ChatAsync"/> only; the other LLM-driving verbs are
/// added as later slices need them.
/// </summary>
public interface IInoGateway
{
    /// <summary>
    /// Recent synapse journal entries backing the inspector drawer. Pass
    /// <paramref name="neuronId"/> to filter to fires where the target or source
    /// matches, or null for the process-wide tail.
    /// </summary>
    Task<IReadOnlyList<SynapseJournalEntry>> GetJournalAsync(
        string? neuronId,
        int limit,
        CancellationToken ct = default);

    /// <summary>
    /// Per-neuron fire/broadcast counters since process start. Pass
    /// <paramref name="neuronId"/> to scope to a single neuron.
    /// </summary>
    Task<NeuronMetricsSnapshot> GetMetricsAsync(
        string? neuronId,
        CancellationToken ct = default);

    /// <summary>
    /// Reasoning provenance for a neuron — which mock / LLM produced the last
    /// response the Flutter inspector panel can surface. v0.1 returns a stub
    /// pointing at <c>Ino.Llm.Provider</c>; slice 15 fills in scenario names
    /// for the BDD mock provider.
    /// </summary>
    Task<NeuronReasoning> GetReasoningAsync(
        string neuronId,
        CancellationToken ct = default);

    /// <summary>
    /// Natural-language entry point. Takes user text, routes through Cortex
    /// (eventually; Slice 1 hard-routes to FlightSearch), fires the resulting
    /// typed synapse, and yields one or more reply frames.
    ///
    /// For RFW-rendering routes the gateway emits a skeleton frame immediately
    /// (<see cref="ChatResult.IsSkeleton"/> = true) so the client can paint a
    /// placeholder card shape while the neuron handler runs, then a final
    /// frame with populated data. Text-only routes yield a single frame.
    ///
    /// <paramref name="correlationId"/> ties this turn to an existing
    /// conversation. Pass <c>null</c> or empty for the first turn — the gateway
    /// generates a fresh handle and surfaces it via <see cref="ChatResult.CorrelationId"/>.
    /// Subsequent turns and clarification fires must echo the cached id back so
    /// the gateway routes them to the same neuron activation.
    /// </summary>
    IAsyncEnumerable<ChatResult> ChatAsync(
        string message,
        string userId,
        string? correlationId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Single-shot natural-language entry point used by the AskIno gRPC RPC and
    /// (in a follow-up slice) the MCP server. Resolves the per-(userId, sessionId)
    /// InoNeuron grain and delegates to its AskAsync. ChatAsync is the
    /// streaming variant that adds skeleton frames + RFW unwrap; AskAsync
    /// returns one final response.
    /// </summary>
    Task<InoResponse> AskAsync(
        string prompt,
        string userId,
        string sessionId,
        string? correlationId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Per-user live event feed — every synapse fire + broadcast that flows
    /// through <see cref="IFirePort"/> in the system silo publishes an
    /// <see cref="InoEvent"/> keyed by the caller's user id, and this method
    /// returns those events as they happen. Optional <paramref name="eventTypes"/>
    /// filter narrows the stream to a subset (empty list = all types).
    /// The Flutter Trace view + inspector drawer are the v0.1 consumers.
    /// </summary>
    IAsyncEnumerable<InoEvent> StreamEventsAsync(
        string userId,
        IReadOnlyList<string>? eventTypes,
        CancellationToken ct = default);

    /// <summary>
    /// Direct fire of a typed synapse identified by a verb string. v0.1
    /// understands one verb — <c>ino.core.provide-clarification</c> — which
    /// constructs a <see cref="ProvideClarification"/> from the
    /// <c>field</c> and <c>value</c> args and fires it via
    /// <see cref="IFirePort"/> with <paramref name="correlationId"/> as the
    /// grain primary key (so the fire lands on the conversation-bearing
    /// neuron activation that asked the question).
    /// Future verbs are added as new conversational primitives ship.
    /// </summary>
    Task<FireResult> FireSynapseAsync(
        string verb,
        IReadOnlyDictionary<string, string> args,
        string correlationId,
        string userId,
        CancellationToken ct = default);

    // Inspector debug affordance — Slice C.4
    Task<FireResult> FireTestSynapseAsync(
        string synapseType,
        string payloadJson,
        string sourceNodeId,
        string userId,
        CancellationToken ct = default);

    // ── Inspector E.3 — Slice 3B ──────────────────────────────────────────────

    /// <summary>
    /// Returns pending/approved/rejected L1 proposals visible to
    /// <paramref name="userId"/>. Pass <c>null</c> filter for all statuses.
    /// </summary>
    Task<IReadOnlyList<ProposalEntry>> ListProposalsAsync(
        string userId,
        ProposalStatus? filter,
        int skip,
        int take,
        CancellationToken ct = default);

    /// <summary>
    /// Approve or reject a pending L1 proposal. Broadcasts
    /// <see cref="ProposalDecided"/> so <c>ProposalLog</c> updates its state.
    /// </summary>
    Task DecideProposalAsync(
        string userId,
        string proposalId,
        ProposalStatus decision,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the last <paramref name="count"/> routing decisions for
    /// <paramref name="userId"/> from the <c>CortexJournal</c> grain.
    /// Server-side cap of 20.
    /// </summary>
    Task<IReadOnlyList<RoutingDecision>> ListRoutingDecisionsAsync(
        string userId,
        int count,
        CancellationToken ct = default);

    // ── Slice 4 — RFW two-way callbacks ──────────────────────────────────────

    /// <summary>
    /// Resolve <paramref name="correlationId"/> back to the originating plan
    /// grain (registered when the gateway stamped its outbound RFW response)
    /// and dispatch the event via <see cref="IRfwEventHandler.HandleRfwEventAsync"/>.
    /// Returns the plan's <see cref="NeuronResult"/> — typically carrying a fresh
    /// <see cref="RfwPayload"/> that the gateway streams back to the user as the
    /// next chat frame.
    /// </summary>
    Task<NeuronResult> HandleRfwEventAsync(
        string correlationId,
        string eventName,
        IReadOnlyDictionary<string, string> args,
        CancellationToken ct = default);
}
