using Microsoft.Extensions.AI;

namespace DigitalBrain.Kernel.V2;

[GrainType("digitalbrain.v2.conversation-model")]
public sealed class V2ConversationModelGrain(IChatClient chat) : Grain, IV2ConversationModelGrain
{
    public async Task<V2ConversationModelCompletionResponse> CompleteAsync(
        V2ConversationModelCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ArgumentException("A prompt is required.", nameof(request));

        var response = await chat.GetResponseAsync(BuildPrompt(request), cancellationToken: cancellationToken);
        var text = response.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("The configured conversation model returned an empty response.");
        return new(text, "configured");
    }

    private static string BuildPrompt(V2ConversationModelCompletionRequest request)
    {
        const string guidance =
            "Answer as INO, a friendly and concise workspace assistant. " +
            "Use plain product language. Never expose identifiers, keys, credentials, grants, endpoints, " +
            "feed or protocol metadata, or implementation and infrastructure details.";
        if (request.ConversationHistory.Count == 0) return guidance + "\n\nuser: " + request.Prompt;
        return guidance + "\n\n" + string.Join("\n", request.ConversationHistory) +
               "\nuser: " + request.Prompt;
    }
}
