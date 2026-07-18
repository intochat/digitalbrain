using Orleans.Journaling;

namespace DigitalBrain.Kernel.Conversation;

internal sealed class ConversationGrain(
    [FromKeyedServices("messages")] IDurableList<ChatMessage> messages,
    TimeProvider time)
    : DurableGrain, IConversation
{
    public async Task AppendUserMessageAsync(Guid id, string text, Guid correlationId, CancellationToken ct)
    {
        messages.Add(new ChatMessage(id, ChatRole.User, text, null, correlationId, time.GetUtcNow()));
        PruneMessages();
        await WriteStateAsync(ct);
    }

    public async Task AppendAssistantMessageAsync(Guid id, string? text, string? rfw, Guid correlationId, CancellationToken ct)
    {
        messages.Add(new ChatMessage(id, ChatRole.Assistant, text ?? string.Empty, rfw, correlationId, time.GetUtcNow()));
        PruneMessages();
        await WriteStateAsync(ct);
    }

    private void PruneMessages()
    {
        // 1. Prevent unbounded growth (cap at 500 messages)
        const int MaxMessages = 500;
        while (messages.Count > MaxMessages)
        {
            messages.RemoveAt(0);
        }

        // 2. Clear old heavy RFW envelopes to save memory.
        // We keep RFW JSON only for the most recent 10 RFW messages.
        const int MaxRfwRetention = 10;
        int rfwSeenCount = 0;

        for (int i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            if (!string.IsNullOrEmpty(msg.RfwEnvelopeJson))
            {
                rfwSeenCount++;
                if (rfwSeenCount > MaxRfwRetention)
                {
                    messages[i] = msg with { RfwEnvelopeJson = null };
                }
            }
        }
    }

    public Task<IReadOnlyList<ChatMessage>> RecentAsync(int count, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ChatMessage>>(ConversationQueries.Recent(messages, count));

    public Task<IReadOnlyList<ChatMessage>> SinceAsync(DateTimeOffset since, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ChatMessage>>(ConversationQueries.Since(messages, since));

    public Task<IReadOnlyList<ChatMessage>> SearchAsync(string query, DateTimeOffset? since, DateTimeOffset? until, int limit, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ChatMessage>>(ConversationQueries.Search(messages, query, since, until, limit));
}
