using DigitalBrain.Core.Sdk;

namespace DigitalBrain.Core;

// INO related synapses extracted from the original monolithic file for organization.
// All keep original namespaces and [Alias] for Orleans compatibility.

[GenerateSerializer]
[Alias("DigitalBrain.Core.InoRequest")]
public record InoRequest(
    string Prompt,
    string? ClientId = null,
    string? WorkspaceId = null) : Synapse(nameof(InoRequest), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.InoResponse")]
public record InoResponse(string Prompt, string Response, string[] UsedTaskIds) : Synapse(nameof(InoResponse), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.InoConversationTurn")]
public record InoConversationTurn(
    [property: Id(0)] string ClientId,
    [property: Id(1)] string Role,
    [property: Id(2)] string Text)
    : Synapse(nameof(InoConversationTurn), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.ContextEvidenceRef")]
public record ContextEvidenceRef(
    [property: Id(0)] string EvidenceId,
    [property: Id(1)] string SourceKind,
    [property: Id(2)] string SourceId,
    [property: Id(3)] string TrustLevel,
    [property: Id(4)] string? CorrelationId = null,
    [property: Id(5)] string? CausationId = null);

[GenerateSerializer]
[Alias("DigitalBrain.Core.ContextPacketSelected")]
public record ContextPacketSelected(
    [property: Id(0)] string PacketId,
    [property: Id(1)] string WorkspaceId,
    [property: Id(2)] IReadOnlyList<ContextEvidenceRef> Evidence,
    [property: Id(3)] int EstimatedSize) : Synapse(nameof(ContextPacketSelected), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.MemorySummary")]
public record MemorySummary(
    string Topic,
    string Summary,
    DateTimeOffset At,
    string? WorkspaceId = null,
    string? SourceKind = null,
    string? TrustLevel = null,
    string? Origin = null) : Synapse(nameof(MemorySummary), At);

[GenerateSerializer]
[Alias("DigitalBrain.Core.InoAction")]
public record InoAction(
    string Label,
    string? FollowUpPrompt = null,
    string? SynapseType = null,
    IReadOnlyDictionary<string, object?>? Props = null
);

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

[GenerateSerializer]
[Alias("DigitalBrain.Core.InoInteractResult")]
public record InoInteractResult(
    string Prompt,
    string ResponseText,
    string? ClassifiedIntent = null,
    double IntentConfidence = 0.0,
    string? ClientId = null,
    string? WorkspaceId = null,
    IReadOnlyList<string> UsedTaskIds = null!,
    IReadOnlyList<string> RecentMemoryTopics = null!,
    IReadOnlyList<InoAction> AvailableActions = null!,
    IReadOnlyList<SelfEvolutionProposalPending> PendingProposals = null!,
    DateTimeOffset Timestamp = default
) : Synapse(nameof(InoInteractResult), Timestamp);

[Alias("DigitalBrain.Core.IInoNeuron")]
public interface IInoNeuron : INeuron, IHandle<InoRequest>, IHandle<TabularDataIngested>, IHandle<DbSchemaInspected>
{
    [Alias("AskAsync")]
    Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default);

    [Alias("InteractAsync")]
    Task<InoInteractResult> InteractAsync(InoInteractRequest request, CancellationToken cancellationToken = default);
}

// Phase 1+: Typed tool results (replaces plain strings for determinism and auth visibility).
public abstract record ToolResult
{
    public sealed record Success(string Content) : ToolResult;
    public sealed record NeedsAuth(string Provider, string ClientId, string Message) : ToolResult;
    public sealed record Denied(string Provider, string Reason) : ToolResult;
    public sealed record Failed(string Provider, string Error, bool Retryable = true) : ToolResult;
}

// Phase 0 observability synapses for making Ino tool usage and auth requirements first-class and visible in traces/journals.
[GenerateSerializer]
[Alias("DigitalBrain.Core.InoToolCallStarted")]
public record InoToolCallStarted(
    string ToolName,
    string? Provider = null,
    string? ClientId = null,
    string? WorkspaceId = null) : Synapse(nameof(InoToolCallStarted), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.InoToolCallCompleted")]
public record InoToolCallCompleted(
    string ToolName,
    string? ResultSummary = null,
    string? Provider = null,
    string? ClientId = null,
    string? WorkspaceId = null) : Synapse(nameof(InoToolCallCompleted), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.InoToolCallFailed")]
public record InoToolCallFailed(
    string ToolName,
    string? Error = null,
    string? Provider = null,
    string? ClientId = null,
    string? WorkspaceId = null) : Synapse(nameof(InoToolCallFailed), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.InoConnectorAuthRequired")]
public record InoConnectorAuthRequired(
    string Provider,
    string? ClientId = null,
    string? WorkspaceId = null,
    string? Message = null) : Synapse(nameof(InoConnectorAuthRequired), DateTimeOffset.UtcNow);
