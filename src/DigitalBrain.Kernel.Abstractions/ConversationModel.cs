using Orleans;

namespace DigitalBrain.Kernel.Runtime;

[GenerateSerializer, Alias("digitalbrain.v2.conversation-model-request")]
public sealed record ConversationModelCompletionRequest(
    [property: Id(0)] string Prompt,
    [property: Id(1)] IReadOnlyList<string> ConversationHistory,
    [property: Id(2)] IReadOnlyList<ConversationModelToolOutcome>? ToolOutcomes = null);

[GenerateSerializer, Alias("digitalbrain.v2.conversation-model-tool-outcome")]
public sealed record ConversationModelToolOutcome(
    [property: Id(0)] string Kind,
    [property: Id(1)] string? Content,
    [property: Id(2)] string? SafeReason);

[GenerateSerializer, Alias("digitalbrain.v2.conversation-model-response")]
public sealed record ConversationModelCompletionResponse(
    [property: Id(0)] string Text,
    [property: Id(1)] string Model);

public enum SemanticMutationKind
{
    Clarify,
    GmailSend,
    SalesforceFieldUpdate,
    Unsupported
}

[GenerateSerializer, Alias("digitalbrain.v2.semantic-mutation-request")]
public sealed record SemanticMutationRequest(
    [property: Id(0)] string ActorScope,
    [property: Id(1)] string ConversationId,
    [property: Id(2)] SemanticProvider Provider,
    [property: Id(3)] string Prompt);

[GenerateSerializer, Alias("digitalbrain.v2.semantic-mutation-proposal")]
public sealed record SemanticMutationProposal(
    [property: Id(0)] SemanticMutationKind Kind,
    [property: Id(1)] string? Recipient = null,
    [property: Id(2)] string? Subject = null,
    [property: Id(3)] string? Body = null,
    [property: Id(4)] string? Entity = null,
    [property: Id(5)] string? RecordId = null,
    [property: Id(6)] string? Field = null,
    [property: Id(7)] string? NewValue = null,
    [property: Id(8)] string? Clarification = null);

[Alias("digitalbrain.v2.conversation-model-grain")]
public interface IConversationModelGrain : IGrainWithStringKey
{
    [Alias("ResolveIntentAsync")]
    Task<SemanticIntentProposal> ResolveIntentAsync(
        SemanticIntentRequest request,
        CancellationToken cancellationToken = default);

    [Alias("ResolveMutationAsync")]
    Task<SemanticMutationProposal> ResolveMutationAsync(
        SemanticMutationRequest request,
        CancellationToken cancellationToken = default);

    [Alias("CompleteAsync")]
    Task<ConversationModelCompletionResponse> CompleteAsync(
        ConversationModelCompletionRequest request,
        CancellationToken cancellationToken = default);
}
