using Orleans;

namespace DigitalBrain.Kernel.V2;

[GenerateSerializer, Alias("digitalbrain.v2.conversation-model-request")]
public sealed record V2ConversationModelCompletionRequest(
    [property: Id(0)] string Prompt,
    [property: Id(1)] IReadOnlyList<string> ConversationHistory);

[GenerateSerializer, Alias("digitalbrain.v2.conversation-model-response")]
public sealed record V2ConversationModelCompletionResponse(
    [property: Id(0)] string Text,
    [property: Id(1)] string Model);

[Alias("digitalbrain.v2.conversation-model-grain")]
public interface IV2ConversationModelGrain : IGrainWithStringKey
{
    [Alias("CompleteAsync")]
    Task<V2ConversationModelCompletionResponse> CompleteAsync(
        V2ConversationModelCompletionRequest request,
        CancellationToken cancellationToken = default);
}
