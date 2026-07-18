using DigitalBrain.Core.Simulation;
using DigitalBrain.Core.Synapses;

namespace DigitalBrain.Abstractions.Ino;

[GenerateSerializer]
public record ConsoleInput([property: Id(0)] string Text) : Synapse;

[GenerateSerializer]
public sealed record ChatResponse(
    [property: Id(0)] string Text, 
    [property: Id(1)] bool Success,
    [property: Id(2)] string? ModelName = null,
    [property: Id(3)] long? InputTokenCount = null,
    [property: Id(4)] long? OutputTokenCount = null) : Synapse;

public sealed record InoCapabilityInvocationResult(
    string Capability,
    string Status,
    Guid ApprovalId,
    string ActionDescription,
    string Message);

public static class InoCapabilityNames
{
    public const string RequestSimulationRun = "request_simulation_run";
}

[GenerateSerializer]
public sealed record RequestSimulationRunApproval([property: Id(0)] string SimulationId) : Synapse;

[GenerateSerializer]
public sealed record ApprovedSimulationRun(
    [property: Id(0)] Guid ApprovalId,
    [property: Id(1)] string SimulationId) : Synapse;

[GenerateSerializer]
public sealed record SimulationRunApprovalRejected(
    [property: Id(0)] Guid ApprovalId,
    [property: Id(1)] string SimulationId,
    [property: Id(2)] string Reason) : Synapse;

[GenerateSerializer]
public sealed record SimulationRunRouted(
    [property: Id(0)] Guid ApprovalId,
    [property: Id(1)] string SimulationId,
    [property: Id(2)] string SimulationNeuronType,
    [property: Id(3)] string SimulationNeuronKey) : Synapse;

[GenerateSerializer]
public sealed record SimulationRunObserved(
    [property: Id(0)] Guid ApprovalId,
    [property: Id(1)] string SimulationId,
    [property: Id(2)] bool Passed,
    [property: Id(3)] IReadOnlyList<string> Diagnostics,
    [property: Id(4)] long ElapsedMs) : Synapse;

[GenerateSerializer]
public sealed record SimulationRunOutcomeSnapshot(
    [property: Id(0)] Guid ApprovalId,
    [property: Id(1)] string SimulationId,
    [property: Id(2)] Guid CorrelationId,
    [property: Id(3)] Guid RunSynapseId,
    [property: Id(4)] string Status,
    [property: Id(5)] bool? Passed,
    [property: Id(6)] IReadOnlyList<string> Diagnostics,
    [property: Id(7)] IReadOnlyList<string> ExpectedSynapses,
    [property: Id(8)] IReadOnlyList<string> ObservedSynapseTypes,
    [property: Id(9)] long? ElapsedMs,
    [property: Id(10)] DateTimeOffset RecordedAt);

public static class GeneratedArtifactDraftKinds
{
    public const string InoFile = "ino_file";
    public const string ExperienceBundleDraft = "experience_bundle_draft";
    public const string NeuronSourceDraft = "neuron_source_draft";
    public const string SynapseSourceDraft = "synapse_source_draft";
}

public static class GeneratedArtifactDraftApprovalStates
{
    public const string Draft = "draft";
}

[GenerateSerializer]
public sealed record GeneratedArtifactDraft(
    [property: Id(0)] Guid DraftId,
    [property: Id(1)] string ArtifactKind,
    [property: Id(2)] string TargetPath,
    [property: Id(3)] string Content,
    [property: Id(4)] IReadOnlyList<string> RelatedSimulationIds,
    [property: Id(5)] IReadOnlyList<string> ExpectedSynapses,
    [property: Id(6)] string ApprovalState);

[GenerateSerializer]
public sealed record GeneratedSimulationDraft(
    [property: Id(0)] string SimulationId,
    [property: Id(1)] string Name,
    [property: Id(2)] string Summary,
    [property: Id(3)] SimulationSpec Spec,
    [property: Id(4)] IReadOnlyList<string> Workflows,
    [property: Id(5)] IReadOnlyList<string> Intents,
    [property: Id(6)] IReadOnlyList<string> Tags,
    [property: Id(7)] string Source)
{
    public SimulationDescriptor ToDescriptor() =>
        new(SimulationId, Name, Summary, Spec, Workflows, Intents, Tags, Source);
}

[GenerateSerializer]
public sealed record GeneratedArtifactDraftPlan(
    [property: Id(0)] GeneratedArtifactDraft Artifact,
    [property: Id(1)] GeneratedSimulationDraft Simulation,
    [property: Id(2)] IReadOnlyList<string> Guardrails);

public static class InoStateKeys
{
    public const string RecentSimulationRuns = "ino.simulation_runs.recent";
    public const string RecentGeneratedDraftIds = "ino.generated_drafts.recent";

    public static string PendingSimulationRunApproval(Guid approvalId) =>
        $"ino.pending_simulation_run.{approvalId:N}.simulation_id";

    public static string SimulationRunCorrelation(Guid correlationId) =>
        $"ino.simulation_run.correlation.{correlationId:N}.approval_id";

    public static string SimulationRunSynapse(Guid approvalId) =>
        $"ino.simulation_run.{approvalId:N}.run_synapse_id";

    public static string GeneratedDraft(Guid draftId) =>
        $"ino.generated_draft.{draftId:N}.json";
}

public static class InoContextKeys
{
    public const string CurrentGoal = "context.current_goal";
    public const string RecentFiles = "context.recent_files";
    public const string GitBranch = "context.git_branch";
    public const string GitDirty = "context.git_dirty";
    public const string LastBuildProject = "context.last_build_project";
    public const string LastBuildStatus = "context.last_build_status";
    public const string LastTestSuite = "context.last_test_suite";
    public const string LastTestStatus = "context.last_test_status";
    public const string ActiveSessions = "context.active_sessions";
    public const string PendingApprovals = "context.pending_approvals";
    public const string CurrentWorkspace = "context.current_workspace";
    public const string LastSummary = "context.last_summary";
    public const string VisibilityScope = "context.visibility_scope";
    
    public const string AgentPromptCurrent = "agent.prompt.current";
    public const string GroupChatTranscript = "groupchat.transcript";
    public const string RoslynLoaded = "roslyn.loaded";
    public const string RoslynWorkspacePath = "roslyn.workspace_path";
    public const string RoslynProjectCount = "roslyn.project_count";
    public const string RoslynCallGraph = "roslyn.call_graph";
    public const string RoslynReverseCallGraph = "roslyn.reverse_call_graph";
    public const string RoslynInheritanceTree = "roslyn.inheritance_tree";

    public static string PendingApprovalDescription(string approvalId)
        => $"context.pending_approval.{approvalId}.description";
    public static string PendingApprovalCapability(string approvalId)
        => $"context.pending_approval.{approvalId}.capability";
    public static string PendingApprovalSubject(string approvalId)
        => $"context.pending_approval.{approvalId}.subject";
}

[GenerateSerializer]
public sealed record SpawnHelloWorld : Synapse;

[GenerateSerializer]
public sealed record RunTests([property: Id(0)] string NeuronName) : Synapse;

[GenerateSerializer]
public sealed record InoConsoleStarted(
    [property: Id(0)] string ConsoleType, 
    [property: Id(1)] string Title, 
    [property: Id(2)] bool IsSuccess) : Synapse;

[GenerateSerializer]
public sealed record ApproveGeneratedArtifactDraft([property: Id(0)] Guid DraftId) : Synapse;

[GenerateSerializer]
public sealed record GeneratedArtifactDraftPersisted(
    [property: Id(0)] Guid DraftId,
    [property: Id(1)] string TargetPath,
    [property: Id(2)] string ArtifactKind) : Synapse;

[GenerateSerializer]
public sealed record GeneratedArtifactDraftApprovalRejected(
    [property: Id(0)] Guid DraftId,
    [property: Id(1)] string Reason) : Synapse;

