using System.Net.ServerSentEvents;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;

namespace DigitalBrain.UiEdge;

internal static class ChatEventFeed
{
    public static IAsyncEnumerable<SseItem<ChatTurnEvent>> WatchChatTurnsAsync(
        OwnerSessionJournal sessionJournal,
        string chatName,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionJournal);

        return JournalProjection.WatchAsync(
            token => sessionJournal.WatchChatOutgoingAsync(chatName, afterSequence, token),
            UiEdgeContract.ChatTurnEvent,
            ProjectTurn,
            cancellationToken);
    }

    private static ChatTurnEvent? ProjectTurn(SynapseDelivery delivery)
    {
        ChatTurnEvent Turn(bool fromUser, string text, CommandId command, string synapseName, NeuronId chat)
            => new(
                delivery.Sequence,
                fromUser,
                text,
                command.ToString(),
                synapseName,
                chat.ToString(),
                delivery.Caller.ToString(),
                delivery.CorrelationId.ToString(),
                delivery.Timestamp);

        return delivery.Synapse switch
        {
            UserMessaged messaged =>
                Turn(true, messaged.Text, messaged.CommandId, nameof(UserMessaged), messaged.Chat),
            AssistantResponded responded =>
                Turn(false, responded.Text, responded.CommandId, nameof(AssistantResponded), responded.Chat),
            _ => null,
        };
    }
}
