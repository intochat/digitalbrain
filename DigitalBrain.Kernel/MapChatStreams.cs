using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Kernel;

internal static class ChatStreamsHttpMaps
{
    internal static readonly TimeSpan TurnBudget =
        TimeSpan.Parse(NeuronCallTimeouts.LongRunning, System.Globalization.CultureInfo.InvariantCulture);

    public static IEndpointRouteBuilder MapChatStreams(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(
            HttpSurfacePaths.StreamMessagePath,
            static async Task (
                HttpContext http,
                string chatName,
                SendMessageRequest request,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(chatName);
                ArgumentNullException.ThrowIfNull(http);
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(request.Text))
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                await SseResponse.WriteAsync(
                    http.Response,
                    StreamDeltasAsync(brain, chatName, request.Text, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            });

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

    private static async IAsyncEnumerable<SseItem<ChatResponseUpdate>> StreamDeltasAsync(
        IDigitalBrain brain,
        string chatName,
        string text,
        [EnumeratorCancellation] CancellationToken requestAborted)
    {
        using var turn = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        turn.CancelAfter(TurnBudget);

        var command = CommandId.New();
        await foreach (var chunk in brain.GetGrainProxy<IChat>(chatName)
            .SendStreaming(new SendMessage(command, text), turn.Token)
            .ConfigureAwait(false))
        {
            turn.Token.ThrowIfCancellationRequested();
            yield return new SseItem<ChatResponseUpdate>(chunk, HttpSurfacePaths.ChatDeltaEvent);
        }
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
