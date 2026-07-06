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
[Alias("DigitalBrain.Core.SelfEvolutionProposal")]
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

public static class SelfEvolutionNeuronIds
{
    public const string Main = "self-evolution-main";
}

public static class SelfEvolutionApplyVia
{
    public const string MarketplaceInstall = "marketplace.install";
    public const string AutomationDefineReaction = "automation.define-reaction";
    public const string FoundryRun = "foundry.run";
    public const string FoundryDeploy = "foundry.deploy";
}

[Alias("DigitalBrain.Core.ISelfEvolutionNeuron")]
public interface ISelfEvolutionNeuron : INeuron, IHandle<SelfEvolutionProposal>, IHandle<SelfEvolutionDecision> { }

/// <summary>A valid proposal entered the approval queue and is awaiting a decision.</summary>
[GenerateSerializer]
[Alias("DigitalBrain.Core.SelfEvolutionProposalPending")]
public record SelfEvolutionProposalPending(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] string ApplyVia,
    [property: Id(2)] SelfEvolutionRisk Risk) : Synapse(nameof(SelfEvolutionProposalPending), DateTimeOffset.UtcNow);

/// <summary>A malformed or duplicate proposal was recorded but not made approvable.</summary>
[GenerateSerializer]
[Alias("DigitalBrain.Core.SelfEvolutionProposalRejected")]
public record SelfEvolutionProposalRejected(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] string Reason) : Synapse(nameof(SelfEvolutionProposalRejected), DateTimeOffset.UtcNow);

/// <summary>A proposal expired before it could be approved.</summary>
[GenerateSerializer]
[Alias("DigitalBrain.Core.SelfEvolutionProposalExpired")]
public record SelfEvolutionProposalExpired(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] DateTimeOffset? ExpiresAt) : Synapse(nameof(SelfEvolutionProposalExpired), DateTimeOffset.UtcNow);

/// <summary>
/// The explicit approve/reject decision for a staged <see cref="SelfEvolutionProposal"/>.
/// <paramref name="DecidedBy"/> identifies the approver (user id or system principal) so the
/// journal records who consented to the change.
/// </summary>
[GenerateSerializer]
[Alias("DigitalBrain.Core.SelfEvolutionDecision")]
public record SelfEvolutionDecision(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] bool Approved,
    [property: Id(2)] string DecidedBy,
    [property: Id(3)] string Reason = "") : Synapse(nameof(SelfEvolutionDecision), DateTimeOffset.UtcNow);

/// <summary>A decision passed validation and was recorded against a pending proposal.</summary>
[GenerateSerializer]
[Alias("DigitalBrain.Core.SelfEvolutionDecisionRecorded")]
public record SelfEvolutionDecisionRecorded(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] bool Approved,
    [property: Id(2)] string DecidedBy,
    [property: Id(3)] string Reason = "") : Synapse(nameof(SelfEvolutionDecisionRecorded), DateTimeOffset.UtcNow);

/// <summary>A decision was ignored because it did not match an approvable pending proposal.</summary>
[GenerateSerializer]
[Alias("DigitalBrain.Core.SelfEvolutionDecisionRejected")]
public record SelfEvolutionDecisionRejected(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] string Reason) : Synapse(nameof(SelfEvolutionDecisionRejected), DateTimeOffset.UtcNow);

/// <summary>The journaled result returned by an allowlisted self-evolution apply handler.</summary>
[GenerateSerializer]
[Alias("DigitalBrain.Core.SelfEvolutionApplyResult")]
public record SelfEvolutionApplyResult(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] string ApplyVia,
    [property: Id(2)] bool Succeeded,
    [property: Id(3)] string Details,
    [property: Id(4)] string? RollbackCheckpointId = null) : Synapse(nameof(SelfEvolutionApplyResult), DateTimeOffset.UtcNow);
/// <summary>An approved apply failed and a concrete checkpoint is available for rollback.</summary>
[GenerateSerializer]
[Alias("DigitalBrain.Core.SelfEvolutionRollbackRequired")]
public record SelfEvolutionRollbackRequired(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] string ApplyVia,
    [property: Id(2)] string CheckpointId,
    [property: Id(3)] string Reason) : Synapse(nameof(SelfEvolutionRollbackRequired), DateTimeOffset.UtcNow);
