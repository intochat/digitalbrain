namespace DigitalBrain.Core;

// Self-evolution rails: every autonomous change to the running system is staged as a
// SelfEvolutionProposal and only applied after an explicit SelfEvolutionDecision.
// These records were introduced with SoftwareEngineeringClosedLoopNeuron's staging flow
// (see StageSelfEvolutionProposalAsync) and are the wire vocabulary for the
// propose -> approve -> apply -> rollback loop. Keep them additive: downstream journals
// persist these synapses, so field removals/renames are wire-breaking.

/// <summary>Blast radius of a proposed self-modification, from least to most disruptive.</summary>
public enum SelfEvolutionRisk
{
    /// <summary>No runtime behavior change (docs, telemetry, config defaults).</summary>
    None,
    /// <summary>New behavior added via pack install/embodiment; existing behavior untouched.</summary>
    PackInstall,
    /// <summary>Generated code executes in-process (foundry Run tier, automation scripts).</summary>
    InProcessCode,
    /// <summary>Apply requires restarting kernel resource(s) (deploy tier, host wiring).</summary>
    KernelRestart
}

/// <summary>
/// A staged, journal-visible proposal for the system to modify itself. Nothing should act on
/// the proposed change until a matching <see cref="SelfEvolutionDecision"/> approves it.
/// </summary>
[GenerateSerializer]
public record SelfEvolutionProposal(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] string Scope,
    [property: Id(2)] string Rationale,
    [property: Id(3)] string ProposedChange,
    [property: Id(4)] string ApplyVia,
    [property: Id(5)] SelfEvolutionRisk Risk,
    [property: Id(6)] bool RequiresHumanApproval,
    [property: Id(7)] string RollbackPlan,
    [property: Id(8)] string Origin,
    [property: Id(9)] DateTimeOffset? ExpiresAt = null) : Synapse(nameof(SelfEvolutionProposal), DateTimeOffset.UtcNow);

/// <summary>
/// The explicit approve/reject decision for a staged <see cref="SelfEvolutionProposal"/>.
/// <paramref name="DecidedBy"/> identifies the approver (user id or system principal) so the
/// journal records who consented to the change.
/// </summary>
[GenerateSerializer]
public record SelfEvolutionDecision(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] bool Approved,
    [property: Id(2)] string DecidedBy,
    [property: Id(3)] string Reason = "") : Synapse(nameof(SelfEvolutionDecision), DateTimeOffset.UtcNow);
