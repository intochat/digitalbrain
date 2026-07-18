namespace DigitalBrain.Kernel.Conversation;

internal static class ConversationQueries
{
    internal static ChatMessage[] Recent(IEnumerable<ChatMessage> messages, int count)
        => messages.TakeLast(count).ToArray();

    internal static ChatMessage[] Since(IEnumerable<ChatMessage> messages, DateTimeOffset since)
        => messages.Where(m => m.Timestamp >= since).ToArray();

    internal static ChatMessage[] Search(
        IEnumerable<ChatMessage> messages,
        string query,
        DateTimeOffset? since,
        DateTimeOffset? until,
        int limit)
        => messages
            .Where(m => (since is null || m.Timestamp >= since) && (until is null || m.Timestamp <= until))
            .Where(m => m.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .ToArray();
}
