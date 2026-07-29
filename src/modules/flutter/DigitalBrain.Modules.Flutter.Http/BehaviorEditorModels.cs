namespace DigitalBrain.UI;

internal sealed record BehaviorEditorDocument(
    string BehaviorId,
    string Status,
    string? ProposedArtifactHash,
    string? ActiveArtifactHash,
    string? PriorArtifactHash,
    string? LastCompileFailure,
    bool TestsPassed,
    bool IsApproved,
    string? LastExecutionOutcome,
    string ProgramSource,
    string FeatureName,
    string FeatureText,
    string DisplayName,
    string Description);

internal sealed record ProposeBehaviorRequest(
    string ProgramSource,
    string FeatureText,
    string FeatureName = "install",
    string DisplayName = "",
    string Description = "");

internal sealed record RunBehaviorTestsRequest(string ArtifactHash);

internal sealed record ApproveBehaviorRequest(string ArtifactHash, string ApprovalId);
