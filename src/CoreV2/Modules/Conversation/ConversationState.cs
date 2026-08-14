using Brain.Modules.Conversation.Contracts;

namespace Brain.Modules.Conversation;

[GenerateSerializer]
public sealed class ConversationState
{
    [Id(0)]
    public string ConversationId { get; set; } = string.Empty;

    [Id(1)]
    public List<ConversationMessage> Messages { get; set; } = [];

    [Id(2)]
    public HashSet<string> ProcessedRequests { get; set; } = new(StringComparer.Ordinal);
}
