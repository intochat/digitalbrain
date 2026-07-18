using Ino.Core;

namespace Ino.Kernel.Contracts;

/// <summary>
/// Phase 4 Slice E foundation: emitted by <c>MissedIntentTracker</c> when a
/// user has produced enough near-duplicate <c>UnroutedIntent</c>s to suggest
/// a missing neuron. v0.1 clusters by normalised text (lowercase + trim
/// + collapse whitespace) — embedding-based fuzzy clustering is post-v0.1.
///
/// A future <c>CreatorNeuron</c> in <c>Ino.Domains.Genesis</c> will subscribe
/// to this broadcast, draft a <see cref="INeuronPlan"/> stub via Roslyn
/// scripting, and surface the proposal in the inspector for user approval.
/// For Phase 4 Slice E.1 the broadcast fires but no consumer is wired —
/// tracking the missed-intent volume is the unblock; turning a proposal
/// into a runtime-registered neuron lands in Slice E.2.
/// </summary>
[GenerateSerializer]
public sealed record L1Proposal(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] string UserId,
    [property: Id(2)] string ClusterKey,
    [property: Id(3)] string ExamplePrompt,
    [property: Id(4)] int Occurrences,
    [property: Id(5)] DateTimeOffset ProposedAt) : ISynapse;
