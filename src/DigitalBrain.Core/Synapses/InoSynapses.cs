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
[Alias("DigitalBrain.Core.MemorySummary")]
public record MemorySummary(
    string Topic,
    string Summary,
    DateTimeOffset At,
    string? WorkspaceId = null) : Synapse(nameof(MemorySummary), At);

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
