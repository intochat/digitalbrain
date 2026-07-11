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

[Alias("digitalbrain.v2.conversation-model-grain")]
public interface IConversationModelGrain : IGrainWithStringKey
{
    [Alias("ResolveIntentAsync")]
    Task<SemanticIntentProposal> ResolveIntentAsync(
        SemanticIntentRequest request,
        CancellationToken cancellationToken = default);

    [Alias("CompleteAsync")]
    Task<ConversationModelCompletionResponse> CompleteAsync(
        ConversationModelCompletionRequest request,
        CancellationToken cancellationToken = default);
}
