namespace DigitalBrain.OS.UiEdge;

internal sealed record BehaviorLibraryDocument(
    IReadOnlyList<BehaviorLibraryItem> Items);

internal sealed record BehaviorLibraryItem(
    string BehaviorId,
    string DisplayName,
    string Description,
    string Status,
    string RunState,
    bool ActivationGateOpen,
    string? ActiveArtifactHash,
    string? Overview,
    IReadOnlyList<string> ScenarioTitles,
    string Health);

internal sealed record BehaviorEditorDocument(
    string BehaviorId,
    string Status,
    string RunState,
    bool ActivationGateOpen,
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
    string Description,
    string Overview,
    string? ActiveSignatureHex,
    int ActiveTaskCount,
    IReadOnlyList<BehaviorScenarioDocument> Scenarios,
    IReadOnlyList<BehaviorBindingDocument> Bindings,
    IReadOnlyList<BehaviorRevisionDocument> Revisions);

internal sealed record BehaviorScenarioDocument(
    string ScenarioId,
    string Title,
    string BindingKey,
    bool? Passed,
    string? Detail);

internal sealed record BehaviorBindingDocument(
    string BindingId,
    string SourceModule,
    string SourceSynapse,
    string TargetCase,
    string ContractVersion,
    bool Enabled,
    string ConfigurationHint);

internal sealed record BehaviorRevisionDocument(
    string Role,
    string? ArtifactHash,
    string? SignatureHex,
    string Status,
    bool IsActive);

internal sealed record ProposeBehaviorRequest(
    string ProgramSource,
    string FeatureText,
    string FeatureName = "install",
    string DisplayName = "",
    string Description = "");

internal sealed record RunBehaviorTestsRequest(string ArtifactHash);

internal sealed record ApproveBehaviorRequest(string ArtifactHash, string ApprovalId);

internal sealed record ActivateBehaviorRequest(string ArtifactHash);

internal sealed record RunOnceBehaviorRequest(string TriggerTypeName, string TriggerJson);

internal sealed record SetBehaviorBindingRequest(bool Enabled);

internal sealed record BehaviorChangeProposeRequest(string RequestText);

internal sealed record BehaviorScenarioApprovalRequest(
    string ProposalId,
    bool Approved,
    string? FeatureText = null,
    string? FeatureName = null);

internal sealed record BehaviorChangeProposalDocument(
    string ProposalId,
    string BehaviorId,
    string RequestText,
    string ProposedFeatureText,
    string ProposedFeatureName,
    string Status,
    string? DiffSummary);

internal sealed record BehaviorEvent(
    long Sequence,
    string Kind,
    string BehaviorId,
    string CommandId,
    string? ArtifactHash,
    string? Detail,
    DateTimeOffset Timestamp);

internal sealed record RunOnceBehaviorResult(
    bool Succeeded,
    string Outcome,
    BehaviorEditorDocument Document);
