namespace DigitalBrain.AI;

public sealed record BehaviorScenarioProposal(
    string ProposalId,
    string ProposedFeatureText,
    string DiffSummary,
    bool RequiresApproval = true);
