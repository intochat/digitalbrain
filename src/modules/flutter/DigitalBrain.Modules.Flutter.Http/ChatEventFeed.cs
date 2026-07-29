using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;

namespace DigitalBrain.UI;

internal static class ChatEventFeed
{
    public static async IAsyncEnumerable<SseItem<ChatTurnEvent>> WatchChatTurnsAsync(
        OwnerSessionJournal sessionJournal,
        string chatName,
        long afterSequence,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionJournal);
        ArgumentException.ThrowIfNullOrWhiteSpace(chatName);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        await foreach (var batch in sessionJournal.WatchChatOutgoingAsync(chatName, afterSequence, cancellationToken))
        {
            foreach (var turn in ProjectTurns(batch))
            {
                yield return new SseItem<ChatTurnEvent>(turn, UiHttpContract.ChatTurnEvent)
                {
                    EventId = turn.Sequence.ToString(CultureInfo.InvariantCulture),
                };
            }
        }
    }

    private static IEnumerable<ChatTurnEvent> ProjectTurns(JournalRead batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.ResetSnapshot is not null)
        {
            yield break;
        }

        foreach (var delivery in batch.Delta)
        {
            switch (delivery.Synapse)
            {
                case UserMessaged messaged:
                    yield return new ChatTurnEvent(
                        delivery.Sequence,
                        FromUser: true,
                        messaged.Text,
                        messaged.CommandId.ToString(),
                        nameof(UserMessaged),
                        messaged.Chat.ToString(),
                        delivery.Caller.ToString(),
                        delivery.CorrelationId.ToString(),
                        delivery.Timestamp);
                    break;

                case AssistantResponded responded:
                    yield return new ChatTurnEvent(
                        delivery.Sequence,
                        FromUser: false,
                        responded.Text,
                        responded.CommandId.ToString(),
                        nameof(AssistantResponded),
                        responded.Chat.ToString(),
                        delivery.Caller.ToString(),
                        delivery.CorrelationId.ToString(),
                        delivery.Timestamp);
                    break;
            }
        }
    }
}
