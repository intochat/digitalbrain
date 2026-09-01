using System.Runtime.CompilerServices;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;

namespace DigitalBrain.SmartPrompt;

internal sealed class CompanyResearchAgent(IWebSearch webSearch) : Neuron, ICompanyResearchAgent
{
    public async Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages)
    {
        var research = await Research(messages, CancellationToken.None);
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, research));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> RespondStreaming(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var research = await Research(messages, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, research)
        {
            FinishReason = ChatFinishReason.Stop,
        };
    }

    private Task<string> Research(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var company = messages.LastOrDefault()?.Text;
        return webSearch.SearchCompanyJsonAsync(
            string.IsNullOrWhiteSpace(company) ? "unknown company" : company,
            cancellationToken);
    }
}
