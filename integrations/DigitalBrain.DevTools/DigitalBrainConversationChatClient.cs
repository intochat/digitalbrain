using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace DigitalBrain.DevTools;

internal delegate Task<ConversationTurnResult> DigitalBrainTurnInvoker(
    BrainOwnerId owner,
    ConversationRole role,
    ConversationId conversation,
    ConversationTurnId turnId,
    string text,
    CancellationToken cancellationToken);

internal sealed class DigitalBrainConversationChatClient : IChatClient
{
    private readonly BrainOwnerId _owner;
    private readonly ConversationRole _role;
    private readonly DigitalBrainTurnInvoker _invokeTurn;

    public DigitalBrainConversationChatClient(
        BrainOwnerId owner,
        ConversationRole role,
        DigitalBrainTurnInvoker invokeTurn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner.Value, nameof(owner));
        if (!Enum.IsDefined(role))
            throw new ArgumentException("A declared conversation role is required.", nameof(role));
        ArgumentNullException.ThrowIfNull(invokeTurn);

        _owner = owner;
        _role = role;
        _invokeTurn = invokeTurn;
    }

    public DigitalBrainConversationChatClient(
        DigitalBrainSessionFactory sessionFactory,
        BrainOwnerId owner,
        ConversationRole role)
        : this(owner, role, CreateTurnInvoker(sessionFactory))
    {
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        var text = GetLatestUserText(messages);
        var conversation = CreateConversationId(options?.ConversationId);
        var result = await _invokeTurn(
            _owner,
            _role,
            conversation,
            ConversationTurnId.New(),
            text,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, result.Response))
        {
            ConversationId = conversation.Value,
            ResponseId = result.TurnId.ToString(),
            FinishReason = ChatFinishReason.Stop
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text)
        {
            ConversationId = response.ConversationId,
            ResponseId = response.ResponseId,
            FinishReason = ChatFinishReason.Stop
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this)
            ? this
            : null;
    }

    public void Dispose()
    {
    }

    private static DigitalBrainTurnInvoker CreateTurnInvoker(
        DigitalBrainSessionFactory sessionFactory)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        return async (owner, role, conversation, turnId, text, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var session = sessionFactory.Create(owner);
            var result = role switch
            {
                ConversationRole.Fast => await session.Client.Conversations
                    .Fast(conversation)
                    .SubmitTurnAsync(turnId, text),
                ConversationRole.Balanced => await session.Client.Conversations
                    .Balanced(conversation)
                    .SubmitTurnAsync(turnId, text),
                ConversationRole.Reasoning => await session.Client.Conversations
                    .Reasoning(conversation)
                    .SubmitTurnAsync(turnId, text),
                _ => throw new InvalidOperationException(
                    "A declared conversation role is required.")
            };
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        };
    }

    private static string GetLatestUserText(IEnumerable<ChatMessage> messages)
    {
        var userMessage = messages.LastOrDefault(message =>
            message.Role == ChatRole.User &&
            !string.IsNullOrWhiteSpace(message.Text));
        if (userMessage is null)
            throw new InvalidOperationException(
                "A non-empty user message is required.");
        return userMessage.Text;
    }

    private static ConversationId CreateConversationId(string? conversationId) =>
        new(string.IsNullOrWhiteSpace(conversationId)
            ? $"devui-{Guid.NewGuid():N}"
            : conversationId);
}
