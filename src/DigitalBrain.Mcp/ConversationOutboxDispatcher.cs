using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

public sealed class ConversationOutboxDispatcher(
    ConversationStateClient conversations,
    RuntimeSurfaceFeed feed,
    TimeProvider timeProvider)
{
    public async Task<InoConversationSnapshot> DispatchAsync(
        RuntimeRequestContext context,
        CancellationToken cancellationToken = default)
    {
        context = await conversations.ResolveContextAsync(context, cancellationToken).ConfigureAwait(false);
        var neuron = conversations.Conversation(context);
        var state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        foreach (var entry in state.Outbox
                     .Where(outbox => outbox.DispatchedAt is null)
                     .OrderBy(outbox => outbox.CreatedAt)
                     .ThenBy(outbox => outbox.OutboxId, StringComparer.Ordinal)
                     .ToArray())
        {
            if (!string.Equals(entry.Kind, "surface-feed", StringComparison.Ordinal))
                throw new RuntimeStateIntegrityException("unknown conversation outbox kind");
            var snapshot = ConversationStateClient.ToSnapshot(context, state);
            await feed.ProjectConversationAsync(
                context,
                snapshot,
                entry.OutboxId,
                entry.CreatedAt,
                cancellationToken).ConfigureAwait(false);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    state = await neuron.MarkOutboxDispatchedAsync(
                        state.Revision,
                        entry.OutboxId,
                        timeProvider.GetUtcNow()).WaitAsync(cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (RuntimeStateConflictException) when (attempt < 2)
                {
                    state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                    if (state.Outbox.FirstOrDefault(outbox =>
                            string.Equals(outbox.OutboxId, entry.OutboxId, StringComparison.Ordinal))?.DispatchedAt is not null)
                        break;
                }
            }
        }
        return ConversationStateClient.ToSnapshot(context, state);
    }
}
