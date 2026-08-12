using System.Net.ServerSentEvents;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.UI;
using DigitalBrain.Auth;

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
                ArgumentNullException.ThrowIfNull(http);
                ArgumentNullException.ThrowIfNull(sessionJournal);
                cancellationToken.ThrowIfCancellationRequested();

                if (!HttpActor.TryGet(http, out var actor))
                {
                    http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                if (string.IsNullOrWhiteSpace(chatName)
                    || !TryPrincipalResource(actor.PrincipalId, chatName, out var chatInstance))
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                var cursor = afterSequence.GetValueOrDefault();
                if (cursor < 0)
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                await SseResponse.WriteAsync(
                    http.Response,
                    WatchChatTurnsAsync(sessionJournal, chatInstance, cursor, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            });

        return endpoints;
    }

    private static bool TryPrincipalResource(PrincipalId principal, string localName, out string instanceName)
    {
        try
        {
            instanceName = PrincipalScoped.InstanceName(principal, localName);
            return true;
        }
        catch (ArgumentException)
        {
            instanceName = "";
            return false;
        }
    }

    private static IAsyncEnumerable<SseItem<ChatTurnEvent>> WatchChatTurnsAsync(
        OwnerSessionJournal sessionJournal,
        string chatInstance,
        long afterSequence,
        CancellationToken cancellationToken)
        => JournalProjection.WatchAsync(
            token => sessionJournal.WatchChatOutgoingAsync(chatInstance, afterSequence, token),
            HttpSurfacePaths.ChatTurnEvent,
            ProjectTurn,
            cancellationToken);

    internal static ChatTurnEvent? ProjectTurn(SynapseDelivery delivery)
    {
        ChatTurnEvent Turn(
            bool fromUser,
            string text,
            CommandId command,
            string synapseName,
            NeuronId chat,
            ChatButtonOffer[]? buttons,
            ChatChartOffer[]? charts = null,
            ChatTimerOffer[]? timers = null,
            string? turnId = null,
            string? status = null)
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
                timers,
                turnId,
                status);

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
            TurnLifecycle life =>
                Turn(
                    false,
                    life.Detail ?? life.Status.ToString(),
                    life.CommandId,
                    nameof(TurnLifecycle),
                    life.Chat,
                    null,
                    turnId: life.TurnId.ToString(),
                    status: life.Status.ToString()),
            _ => null,
        };
    }
}
