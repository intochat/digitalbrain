namespace DigitalBrain.Kernel.Conversation;

public interface IConversation : IGrainWithStringKey
{
    Task AppendUserMessageAsync(Guid messageId, string text, Guid correlationId, CancellationToken ct);
    Task AppendAssistantMessageAsync(Guid messageId, string? text, string? rfwEnvelopeJson, Guid correlationId, CancellationToken ct);
    Task<IReadOnlyList<ChatMessage>> RecentAsync(int count, CancellationToken ct);
    Task<IReadOnlyList<ChatMessage>> SinceAsync(DateTimeOffset since, CancellationToken ct);
    Task<IReadOnlyList<ChatMessage>> SearchAsync(string query, DateTimeOffset? since, DateTimeOffset? until, int limit, CancellationToken ct);
}
