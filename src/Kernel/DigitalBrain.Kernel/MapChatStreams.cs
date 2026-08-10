using System.Net.ServerSentEvents;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal static class ChatStreamsHttpMaps
{
    public static IEndpointRouteBuilder MapChatStreams(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            HttpSurfacePaths.ChatEventsPath,
            static async Task (
                HttpContext http,
                string chatName,
                long? afterSequence,
                OwnerSessionJournal sessionJournal,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(chatName);
                ArgumentNullException.ThrowIfNull(http);
                ArgumentNullException.ThrowIfNull(sessionJournal);
                cancellationToken.ThrowIfCancellationRequested();

                var cursor = afterSequence.GetValueOrDefault();
                if (cursor < 0)
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                await SseResponse.WriteAsync(
                    http.Response,
                    WatchChatTurnsAsync(sessionJournal, chatName, cursor, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            });

        return endpoints;
    }

    private static IAsyncEnumerable<SseItem<ChatTurnEvent>> WatchChatTurnsAsync(
        OwnerSessionJournal sessionJournal,
        string chatName,
        long afterSequence,
        CancellationToken cancellationToken)
        => JournalProjection.WatchAsync(
            token => sessionJournal.WatchChatOutgoingAsync(chatName, afterSequence, token),
            HttpSurfacePaths.ChatTurnEvent,
            ProjectTurn,
            cancellationToken);

    private static ChatTurnEvent? ProjectTurn(SynapseDelivery delivery)
    {
        ChatTurnEvent Turn(
            bool fromUser,
            string text,
            CommandId command,
            string synapseName,
            NeuronId chat,
            ChatButtonOffer[]? buttons,
            ChatChartOffer[]? charts = null,
            ChatTimerOffer[]? timers = null)
            => new(
                delivery.Sequence,
                fromUser,
                text,
                command.ToString(),
                synapseName,
                chat.ToString(),
                delivery.Caller.ToString(),
                delivery.CorrelationId.ToString(),
                delivery.Timestamp,
                buttons,
                charts,
                timers);

        return delivery.Synapse switch
        {
            UserMessaged messaged =>
                Turn(true, messaged.Text, messaged.CommandId, nameof(UserMessaged), messaged.Chat, null),
            Responded responded =>
                Turn(
                    false,
                    responded.Text,
                    responded.CommandId,
                    nameof(Responded),
                    responded.Chat,
                    responded.Buttons,
                    responded.Charts,
                    responded.Timers),
            _ => null,
        };
    }
}
