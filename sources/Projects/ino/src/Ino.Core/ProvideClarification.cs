namespace Ino.Core;

/// <summary>
/// Kernel-level conversational primitive — the user's answer to an
/// <see cref="AskClarification"/>. Routed via Discovery to the canonical
/// handler for this synapse type; Orleans grain identity (the conversation's
/// correlation_id used as grain primary key) ensures it lands on the same
/// neuron activation that asked the question. The neuron reads its journal
/// to recover the slot context.
///
/// For v0.1 only TripPlannerNeuron is canonical for this type. Multi-domain
/// clarification routing (each domain getting its own canonical handler) is
/// deferred to a post-v0.1 slice that introduces a typed wrapper or a
/// correlation-id-keyed dispatch.
/// </summary>
[GenerateSerializer]
public sealed record ProvideClarification(
    [property: Id(0)] string Field,
    [property: Id(1)] string Value) : ISynapse;
