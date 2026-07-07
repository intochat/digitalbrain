using DigitalBrain.Core.Sdk;

namespace DigitalBrain.Core;

[GenerateSerializer]
[Alias("DigitalBrain.Core.SynapseType")]
public readonly record struct SynapseType([property: Id(0)] string Value);

[GenerateSerializer]
[Alias("DigitalBrain.Core.Synapse")]
public record Synapse(
    [property: Id(0)] string Type,
    [property: Id(1)] DateTimeOffset Timestamp,
    [property: Id(2)] NeuronId? Sender = null,
    [property: Id(3)] NeuronId? Receiver = null,
    [property: Id(4)] bool IsBroadcast = false,
    [property: Id(5)] string? CorrelationId = null
)
{
    [Id(6)] public string SynapseId { get; init; } = Guid.NewGuid().ToString("N");

    [Id(7)] public string? CausationId { get; init; }

    public Synapse Stamp(NeuronId sender, Synapse? cause = null) =>
        this with
        {
            Sender = sender,
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = CorrelationId ?? cause?.CorrelationId ?? cause?.SynapseId ?? SynapseId,
            CausationId = cause?.SynapseId
        };
}

[GenerateSerializer]
[Alias("DigitalBrain.Core.NeuronTelemetry")]
public record NeuronTelemetry(NeuronId Neuron, string Event, int Count = 1) : Synapse(nameof(NeuronTelemetry), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.WiringOptimizationProposed")]
public record WiringOptimizationProposed(string Proposal, string FromNeuron) : Synapse(nameof(WiringOptimizationProposed), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.ExperienceUsed")]
public record ExperienceUsed(
    string Pack,
    string Action,
    string UserId = "anonymous",
    string? SessionId = null) : Synapse(nameof(ExperienceUsed), DateTimeOffset.UtcNow);

// Core system neuron interfaces (everything is a Neuron)
[Alias("DigitalBrain.Core.IAspireNeuron")]
public interface IAspireNeuron : INeuron, IHandle<StartDistributedApp>, IHandle<RestartResource> { }

// Thin common marker for channel neurons (Telegram, Flutter UI, etc.) per item 13.
// Allows discovery and shared patterns (e.g. CorrelationId/CausationId for reply context across channels).
// No methods yet - keeps it thin; specific contracts live in feature contract assemblies
// (ITelegramChatNeuron in DigitalBrain.Telegram, IFlutterUiNeuron in DigitalBrain.Ui.Contracts), not Core.
[Alias("DigitalBrain.Core.IChannelNeuron")]
public interface IChannelNeuron : INeuron
{
}

// IUser contract lives in Core so kernel can run standalone for security/air-gapped scenarios.
// Full user accounts, auth, billing live in the private marketplace service.
[GenerateSerializer]
[Alias("DigitalBrain.Core.UserId")]
public readonly record struct UserId([property: Id(0)] string Value)
{
    public static UserId Anonymous => new("anonymous");
}

[GenerateSerializer]
[Alias("DigitalBrain.Core.LoginRequest")]
public record LoginRequest(
    string Username,
    string Password,
    string ClientId = "flutter") : Synapse(nameof(LoginRequest), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.LoginSucceeded")]
public record LoginSucceeded(
    UserId UserId,
    string SessionId,
    string DisplayName,
    IReadOnlyList<string> Roles,
    string ClientId) : Synapse(nameof(LoginSucceeded), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.LoginFailed")]
public record LoginFailed(
    string Username,
    string Reason,
    string ClientId) : Synapse(nameof(LoginFailed), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.LogoutRequest")]
public record LogoutRequest(
    string SessionId,
    string ClientId = "flutter") : Synapse(nameof(LogoutRequest), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.UserSessionCreated")]
public record UserSessionCreated(
    UserId UserId,
    string SessionId,
    DateTimeOffset ExpiresAt,
    string ClientId) : Synapse(nameof(UserSessionCreated), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.UserSessionEnded")]
public record UserSessionEnded(
    string SessionId,
    string ClientId) : Synapse(nameof(UserSessionEnded), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.CapabilityRegistered")]
public record CapabilityRegistered(
    string Id,
    string Description,
    IReadOnlyList<string> Examples,
    string Tier,
    string? Origin = null) : Synapse(nameof(CapabilityRegistered), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.LocalUserRegistered")]
public record LocalUserRegistered(
    UserId UserId,
    string Username,
    string DisplayName,
    string PasswordHashBase64,
    string PasswordSaltBase64,
    IReadOnlyList<string> Roles) : Synapse(nameof(LocalUserRegistered), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.UserSessionState")]
public record UserSessionState(
    UserId UserId,
    string SessionId,
    string DisplayName,
    IReadOnlyList<string> Roles,
    DateTimeOffset ExpiresAt,
    bool Active);

[Alias("DigitalBrain.Core.IUserGrain")]
public interface IUserGrain : IGrainWithStringKey
{
    [Alias("GetProfileAsync")]
    Task<UserProfile> GetProfileAsync();
    [Alias("HasEntitlementAsync")]
    Task<bool> HasEntitlementAsync(string bundleOrResource, string actionOrCapability);
}

[GenerateSerializer]
[Alias("DigitalBrain.Core.UserProfile")]
public record UserProfile(UserId Id, string DisplayName, IReadOnlyList<string> Roles);

[Alias("DigitalBrain.Core.IMetaOptimizerNeuron")]
public interface IMetaOptimizerNeuron : INeuron, IHandle<NeuronTelemetry>, IHandle<WiringOptimizationProposed> { }

[Alias("DigitalBrain.Core.IGeneratedNeuron")]
public interface IGeneratedNeuron : INeuron { }

[GenerateSerializer]
[Alias("DigitalBrain.Core.LlmPrompt")]
public record LlmPrompt(string Prompt, string? PreferredModel = null) : Synapse(nameof(LlmPrompt), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.LlmResponse")]
public record LlmResponse(string Prompt, string Response, string ModelUsed) : Synapse(nameof(LlmResponse), DateTimeOffset.UtcNow);

[Alias("DigitalBrain.Core.ILlmNeuron")]
public interface ILlmNeuron : INeuron, IHandle<LlmPrompt> { }

[Alias("DigitalBrain.Core.IInoNeuron")]
public interface IInoNeuron : INeuron, IHandle<InoRequest>, IHandle<TabularDataIngested>, IHandle<DbSchemaInspected>
{
    [Alias("AskAsync")]
    Task<string> AskAsync(string prompt);

    /// <summary>
    /// Rich interaction entrypoint. Returns the common InoInteractResult contract.
    /// This is the primary surface for MCP agents and verification tests.
    /// </summary>
    [Alias("InteractAsync")]
    Task<InoInteractResult> InteractAsync(InoInteractRequest request);
}

// Self-awareness: SystemStatus + proposals (MVP for auto diagnose + simulate fix)
[GenerateSerializer]
[Alias("DigitalBrain.Core.SystemLaunched")]
public record SystemLaunched(string SystemName, DateTimeOffset Timestamp) : Synapse(nameof(SystemLaunched), Timestamp);

[GenerateSerializer]
[Alias("DigitalBrain.Core.SystemStatusChanged")]
public record SystemStatusChanged(string Component, string Status, string? Details = null) : Synapse(nameof(SystemStatusChanged), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.FixProposal")]
public record FixProposal(string Issue, string ProposedFix, string From) : Synapse(nameof(FixProposal), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.SimulationResult")]
public record SimulationResult(string Scenario, bool Success, string Details) : Synapse(nameof(SimulationResult), DateTimeOffset.UtcNow);

[Alias("DigitalBrain.Core.ISystemStatus")]
public interface ISystemStatus : INeuron, IHandle<SystemStatusChanged>, IHandle<FixProposal> { }

// Dual journal checkpoints + branching for simulation / time travel.
[GenerateSerializer]
[Alias("DigitalBrain.Core.Checkpoint")]
public record Checkpoint(NeuronId Source, IReadOnlyList<Synapse> Snapshot, DateTimeOffset TakenAt) : Synapse(nameof(Checkpoint), TakenAt);

[GenerateSerializer]
[Alias("DigitalBrain.Core.BranchCreated")]
public record BranchCreated(NeuronId Source, string BranchId) : Synapse(nameof(BranchCreated), DateTimeOffset.UtcNow);

// Task protocol messages (recoverable task lifecycle for INO, MCP actions, orchestration).
// The durable grain impl (IKernelTask) lives in the kernel layer; these messages are universal core protocol.
[GenerateSerializer]
[Alias("DigitalBrain.Core.TaskCreated")]
public record TaskCreated(TaskId TaskId, string Description) : Synapse(nameof(TaskCreated), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.TaskStarted")]
public record TaskStarted(TaskId TaskId) : Synapse(nameof(TaskStarted), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.TaskProgress")]
public record TaskProgress(TaskId TaskId, string Detail) : Synapse(nameof(TaskProgress), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.TaskCompleted")]
public record TaskCompleted(TaskId TaskId, string? Result = null) : Synapse(nameof(TaskCompleted), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.TaskCancelled")]
public record TaskCancelled(TaskId TaskId) : Synapse(nameof(TaskCancelled), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.RunTask")]
public record RunTask(
    TaskId TaskId,
    string Description,
    string UserId = "anonymous",
    string? SessionId = null) : Synapse(nameof(RunTask), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.CancelTask")]
public record CancelTask(
    TaskId TaskId,
    string UserId = "anonymous",
    string? SessionId = null) : Synapse(nameof(CancelTask), DateTimeOffset.UtcNow);

// Rich task state returned by the task grain.
[GenerateSerializer]
[Alias("DigitalBrain.Core.TaskInfo")]
public record TaskInfo(
    [property: Id(0)] TaskId TaskId,
    [property: Id(1)] string Status,
    [property: Id(2)] string? Result = null
);

// INO - the personal ultra-context assistant.
[GenerateSerializer]
[Alias("DigitalBrain.Core.InoRequest")]
public record InoRequest(
    string Prompt,
    string? ClientId = null,
    string? WorkspaceId = null) : Synapse(nameof(InoRequest), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.InoResponse")]
public record InoResponse(string Prompt, string Response, string[] UsedTaskIds) : Synapse(nameof(InoResponse), DateTimeOffset.UtcNow);

// For INO excellent long-term/multi-scale context (summaries from journals).
[GenerateSerializer]
[Alias("DigitalBrain.Core.MemorySummary")]
public record MemorySummary(
    string Topic,
    string Summary,
    DateTimeOffset At,
    string? WorkspaceId = null) : Synapse(nameof(MemorySummary), At);

// =============================================================================
// INO rich interaction contract (MCP agents, external CLIs, test verification)
// This is the common standard for driving + observing INO.
// Product goal: external agents (Claude, Grok, Codex, tests) can reliably verify
// that new features (direct answers, automation-as-apps, proposals, scoping, rail)
// continue to work at live time.
// =============================================================================

/// <summary>
/// Structured action that an agent (or UI) can take next against INO or the system.
/// </summary>
[GenerateSerializer]
[Alias("DigitalBrain.Core.InoAction")]
public record InoAction(
    string Label,
    string? FollowUpPrompt = null,           // natural language to send back as InoRequest
    string? SynapseType = null,
    IReadOnlyDictionary<string, object?>? Props = null
);

/// <summary>
/// Request for a rich, observable interaction with INO.
/// </summary>
[GenerateSerializer]
[Alias("DigitalBrain.Core.InoInteractRequest")]
public record InoInteractRequest(
    string Prompt,
    string? ClientId = null,
    string? WorkspaceId = null,
    bool IncludeProposals = true,
    bool IncludeActions = true,
    int MaxHistory = 5
) : Synapse(nameof(InoInteractRequest), DateTimeOffset.UtcNow);

/// <summary>
/// The standardized result of interacting with INO.
/// Used by MCP, agent harnesses, and contract tests to verify behavior.
/// </summary>
[GenerateSerializer]
[Alias("DigitalBrain.Core.InoInteractResult")]
public record InoInteractResult(
    string Prompt,
    string ResponseText,                     // the direct user-visible answer (key for "no more I'll start..." regression)
    string? ClassifiedIntent = null,
    double IntentConfidence = 0.0,
    string? ClientId = null,
    string? WorkspaceId = null,
    IReadOnlyList<string> UsedTaskIds = null!,
    IReadOnlyList<string> RecentMemoryTopics = null!,
    IReadOnlyList<InoAction> AvailableActions = null!,   // "Run now", "Approve", "Summarize last", etc.
    IReadOnlyList<SelfEvolutionProposalPending> PendingProposals = null!,
    DateTimeOffset Timestamp = default
) : Synapse(nameof(InoInteractResult), Timestamp);

// NuGet + Roslyn architect for closed loops (SEClosedLoopNeuron).
[GenerateSerializer]
[Alias("DigitalBrain.Core.NuGetCommand")]
public record NuGetCommand(string Action, string Target, string Args = "") : Synapse(nameof(NuGetCommand), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.NuGetResult")]
public record NuGetResult(string Target, bool Success, string Output) : Synapse(nameof(NuGetResult), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.ArchitectRequest")]
public record ArchitectRequest(string Path, string Task) : Synapse(nameof(ArchitectRequest), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.ArchitectReport")]
public record ArchitectReport(string Path, string Report) : Synapse(nameof(ArchitectReport), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.ArchitectResult")]
public record ArchitectResult(string Path, string Result, string Task) : Synapse(nameof(ArchitectResult), DateTimeOffset.UtcNow);

// Closed loop request (routed exclusively via run_closed_loop MCP).
[GenerateSerializer]
[Alias("DigitalBrain.Core.ClosedLoopRequest")]
public record ClosedLoopRequest(string LoopType, string Prompt) : Synapse(nameof(ClosedLoopRequest), DateTimeOffset.UtcNow);

[Alias("DigitalBrain.Core.IClosedLoopNeuron")]
public interface IClosedLoopNeuron : INeuron, IHandle<ClosedLoopRequest>, IHandle<ExperienceUsed> { }

// Skill injection from marketplace packs (used by INO assistant for awareness of installed behaviors).
[GenerateSerializer]
[Alias("DigitalBrain.Core.SkillContextInjected")]
public record SkillContextInjected(string SkillPackName, string Description, string Code) : Synapse(nameof(SkillContextInjected), DateTimeOffset.UtcNow);

// Smart ContextNeuron for INO - manages chat, agent, filter, cluster contexts like context providers
[GenerateSerializer]
[Alias("DigitalBrain.Core.ContextUpdate")]
public record ContextUpdate(string ContextName, string Key, string Value) : Synapse(nameof(ContextUpdate), DateTimeOffset.UtcNow);

// A stored semantic memory: the text plus its embedding (empty when no real embedder is configured).
[GenerateSerializer]
[Alias("DigitalBrain.Core.MemoryStored")]
public record MemoryStored(string Text, float[] Embedding) : Synapse(nameof(MemoryStored), DateTimeOffset.UtcNow);

// Dynamic DB support neuron with typed synapses (inspired by .NET 11 Preview 5 EF/file-based + runtime dynamic)
[GenerateSerializer]
[Alias("DigitalBrain.Core.DbConnect")]
public record DbConnect(string ConnectionName, string Provider, string ConnectionString) : Synapse(nameof(DbConnect), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.DbQuery")]
public record DbQuery(string ConnectionName, string Query, string? Result = null) : Synapse(nameof(DbQuery), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.DbInspectSchema")]
public record DbInspectSchema(
    string ConnectionName,
    string Provider,
    string? ConnectionString = null,
    string? SourcePath = null,
    string? ClientId = null,
    string? WorkspaceId = null) : Synapse(nameof(DbInspectSchema), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.DbSchemaInspected")]
public record DbSchemaInspected(
    string ConnectionName,
    string Provider,
    DbSchemaModel? Schema,
    bool Succeeded = true,
    string? Error = null,
    string? ClientId = null,
    string? WorkspaceId = null) : Synapse(nameof(DbSchemaInspected), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.DbSchemaModel")]
public record DbSchemaModel(
    [property: Id(0)] string ConnectionName,
    [property: Id(1)] string Provider,
    [property: Id(2)] IReadOnlyList<DbTable> Tables,
    [property: Id(3)] string? SourcePath = null,
    [property: Id(4)] string? SessionId = null,
    [property: Id(5)] IReadOnlyDictionary<string, string?>? Metadata = null,
    [property: Id(6)] string? WorkspaceId = null);

[GenerateSerializer]
[Alias("DigitalBrain.Core.DbTable")]
public record DbTable(
    [property: Id(0)] string Name,
    [property: Id(1)] string Kind,
    [property: Id(2)] IReadOnlyList<DbColumn> Columns,
    [property: Id(3)] IReadOnlyList<DbForeignKey> ForeignKeys,
    [property: Id(4)] IReadOnlyList<DbIndex> Indexes,
    [property: Id(5)] string? Schema = null,
    [property: Id(6)] IReadOnlyDictionary<string, string?>? Metadata = null);

[GenerateSerializer]
[Alias("DigitalBrain.Core.DbColumn")]
public record DbColumn(
    [property: Id(0)] string Name,
    [property: Id(1)] string? StoreType,
    [property: Id(2)] bool IsNullable,
    [property: Id(3)] int PrimaryKeyOrdinal = 0,
    [property: Id(4)] string? DefaultValue = null,
    [property: Id(5)] int Ordinal = 0,
    [property: Id(6)] IReadOnlyDictionary<string, string?>? Metadata = null);

[GenerateSerializer]
[Alias("DigitalBrain.Core.DbForeignKey")]
public record DbForeignKey(
    [property: Id(0)] string Name,
    [property: Id(1)] string Table,
    [property: Id(2)] IReadOnlyList<string> Columns,
    [property: Id(3)] string PrincipalTable,
    [property: Id(4)] IReadOnlyList<string> PrincipalColumns,
    [property: Id(5)] string? OnUpdate = null,
    [property: Id(6)] string? OnDelete = null,
    [property: Id(7)] string? Match = null,
    [property: Id(8)] IReadOnlyDictionary<string, string?>? Metadata = null);

[GenerateSerializer]
[Alias("DigitalBrain.Core.DbIndex")]
public record DbIndex(
    [property: Id(0)] string Name,
    [property: Id(1)] string Table,
    [property: Id(2)] IReadOnlyList<string> Columns,
    [property: Id(3)] bool IsUnique = false,
    [property: Id(4)] bool IsPartial = false,
    [property: Id(5)] string? Origin = null,
    [property: Id(6)] IReadOnlyDictionary<string, string?>? Metadata = null);

[Alias("DigitalBrain.Core.IDbSupportNeuron")]
public interface IDbSupportNeuron : INeuron, IHandle<DbConnect>, IHandle<DbQuery>, IHandle<DbInspectSchema> { }

// Filter changes - INO/Context must be notified so assistant knows current UI view state
[GenerateSerializer]
[Alias("DigitalBrain.Core.FilterChanged")]
public record FilterChanged(string View, string Filter, string Value) : Synapse(nameof(FilterChanged), DateTimeOffset.UtcNow);

// 3D graph / cluster observation synapses
[GenerateSerializer]
[Alias("DigitalBrain.Core.ClusterActivity")]
public record ClusterActivity(string NodeId, string Activity, double Value) : Synapse(nameof(ClusterActivity), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.ThreeDGraphUpdate")]
public record ThreeDGraphUpdate(string GraphId, string DataJson) : Synapse(nameof(ThreeDGraphUpdate), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.VisualizeDataRequest")]
public record VisualizeDataRequest(
    string Prompt,
    string DataJson,
    string? ChartHint = null,
    string? RequestId = null,
    string UserId = "anonymous",
    string? SessionId = null) : Synapse(nameof(VisualizeDataRequest), DateTimeOffset.UtcNow, CorrelationId: RequestId);

// Company brain skill knowledge ingestion (narrow for process playbooks + transcripts).
// Used to feed raw domain knowledge into context for crystallization.
[GenerateSerializer]
[Alias("DigitalBrain.Core.IngestCompanySource")]
public record IngestCompanySource(string Collection, string SourceId, string Text) : Synapse(nameof(IngestCompanySource), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.CompanySourceIngested")]
public record CompanySourceIngested(string Collection, string SourceId, int ChunkCount) : Synapse(nameof(CompanySourceIngested), DateTimeOffset.UtcNow);

[Alias("DigitalBrain.Core.ICompanyKnowledgeNeuron")]
public interface ICompanyKnowledgeNeuron : INeuron, IHandle<IngestCompanySource> { }

// First-class chart interaction and modification (conversational + selection driven)
[GenerateSerializer]
[Alias("DigitalBrain.Core.ChartCommand")]
public record ChartCommand(string SurfaceId, string Instruction, string? Context = null) : Synapse(nameof(ChartCommand), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.ChartInteraction")]
public record ChartInteraction(string SurfaceId, string Kind, IReadOnlyDictionary<string, object?> Payload) : Synapse(nameof(ChartInteraction), DateTimeOffset.UtcNow);

[Alias("DigitalBrain.Core.IDataVisualizationNeuron")]
public interface IDataVisualizationNeuron : INeuron, IHandle<VisualizeDataRequest> { }

// Chart neuron supports agent metadata for routing + full conversational + selection driven updates.
[Alias("DigitalBrain.Core.IChartNeuron")]
public interface IChartNeuron : IAgent, IHandle<VisualizeDataRequest>, IHandle<ChartCommand>, IHandle<ChartInteraction> { }

// Closed loops for marketplace (UI authoring via Dart MCP + widget tree; SoftwareEngineering runtime mod via Aspire MCP + LLM)


[GenerateSerializer]
[Alias("DigitalBrain.Core.WidgetTreeInspected")]
public record WidgetTreeInspected(string Summary, string TreeJson = "", string App = "flutter_demo") : Synapse(nameof(WidgetTreeInspected), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.UIModificationProposed")]
public record UIModificationProposed(string TargetFileOrWidget, string Rationale, string ProposedDartCode) : Synapse(nameof(UIModificationProposed), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.SystemModificationProposed")]
public record SystemModificationProposed(string Component, string Rationale, string ProposedChange, string ApplyVia = "aspire-restart") : Synapse(nameof(SystemModificationProposed), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.ClosedLoopCompleted")]
public record ClosedLoopCompleted(string LoopType, string Outcome, bool AppliedViaMcpOrMarket) : Synapse(nameof(ClosedLoopCompleted), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.PerformKernelSelfUpdate")]
public record PerformKernelSelfUpdate(string Version = "", int FailAtReplica = 0) : Synapse(nameof(PerformKernelSelfUpdate), DateTimeOffset.UtcNow);

// Salesforce OAuth callback completion (MULTIUSER S1: grain-routed callback, replaces direct
// Program.cs store IO so the completion always reaches the activation that started the flow).
[GenerateSerializer]
[Alias("DigitalBrain.Core.SalesforceOAuthCallback")]
public record SalesforceOAuthCallback(
    [property: Id(0)] string? Code,
    [property: Id(1)] string? State,
    [property: Id(2)] string? Error,
    [property: Id(3)] string? ErrorDescription,
    [property: Id(4)] string FallbackRedirectUri);

[GenerateSerializer]
[Alias("DigitalBrain.Core.SalesforceOAuthCallbackResult")]
public record SalesforceOAuthCallbackResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] string Title,
    [property: Id(2)] string Message);

