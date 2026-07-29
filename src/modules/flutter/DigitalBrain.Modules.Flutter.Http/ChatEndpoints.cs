using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Client;

namespace DigitalBrain.UI;

internal static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChat(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(
            UiHttpContract.SendMessagePath,
            static async Task<IResult> (
                string chatName,
                SendMessageRequest request,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(chatName);
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(request.Text))
                {
                    return Results.BadRequest();
                }

                await brain.Get<IChat>(chatName).Send(new SendMessage(CommandId.New(), request.Text));

                return Results.Accepted();
            });

        endpoints.MapPost(
            UiHttpContract.StreamMessagePath,
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
                    ChatDeltaFeed.StreamDeltasAsync(brain, chatName, request.Text, cancellationToken),
                    cancellationToken);
            });

        endpoints.MapGet(
            UiHttpContract.ChatEventsPath,
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
                    ChatEventFeed.WatchChatTurnsAsync(sessionJournal, chatName, cursor, cancellationToken),
                    cancellationToken);
            });

        return endpoints;
    }
}
