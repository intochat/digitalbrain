using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Client;

namespace DigitalBrain.Ui;

internal static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChat(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(
            UiEdgeContract.SendMessagePath,
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

        endpoints.MapGet(
            UiEdgeContract.ChatEventsPath,
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

                http.Response.Headers.CacheControl = UiEdgeContract.CacheControlNoCache;
                http.Response.ContentType = UiEdgeContract.EventStreamContentType;
                await ChatEventFeed.WriteChatTurnSseAsync(
                    http.Response.Body,
                    sessionJournal,
                    chatName,
                    cursor,
                    cancellationToken);
            });

        return endpoints;
    }
}
