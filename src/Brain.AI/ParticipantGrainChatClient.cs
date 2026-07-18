namespace DigitalBrain.AI;

using System.Runtime.CompilerServices;
using Brain.Contracts;
using Microsoft.Extensions.AI;

internal sealed class ParticipantGrainChatClient : IChatClient
{
    private readonly Func<CommandSynapse<AgentTurnRequest>, Task<AgentTurnResult>> _completeTurn;
    private readonly string _participantName;
    private readonly Func<SynapseMetadata> _metadataFactory;

    public ParticipantGrainChatClient(
        string participantName,
        Func<CommandSynapse<AgentTurnRequest>, Task<AgentTurnResult>> completeTurn,
        Func<SynapseMetadata> metadataFactory)
    {
        _participantName = participantName;
        _completeTurn = completeTurn;
        _metadataFactory = metadataFactory;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var inputText = ExtractLastNonEmptyText(messages);
        var requestId = Guid.NewGuid().ToString("N");
        var metadata = _metadataFactory();
        var result = await _completeTurn(
            new CommandSynapse<AgentTurnRequest>(
                metadata,
                new AgentTurnRequest(requestId, inputText))).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        var message = new ChatMessage(ChatRole.Assistant, result.ResponseText)
        {
            AuthorName = _participantName
        };
        return new ChatResponse(message);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        foreach (var message in response.Messages)
        {
            yield return new ChatResponseUpdate(message.Role, message.Text)
            {
                AuthorName = message.AuthorName
            };
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    private static string ExtractLastNonEmptyText(IEnumerable<ChatMessage> messages)
    {
        string? last = null;
        foreach (var message in messages)
        {
            if (!string.IsNullOrWhiteSpace(message.Text))
                last = message.Text;
        }

        return last ?? string.Empty;
    }
}
