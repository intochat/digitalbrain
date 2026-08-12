using System.Net.ServerSentEvents;
using DigitalBrain.Abstractions;
using DigitalBrain.Auth;

namespace DigitalBrain.Kernel;

internal static class ConversationStreamsHttpMaps
{
    public static IEndpointRouteBuilder MapConversationStreams(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            HttpSurfacePaths.ConversationEventsPath,
            static async Task (
                HttpContext http,
                string conversationName,
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

                if (string.IsNullOrWhiteSpace(conversationName)
                    || !TryPrincipalResource(actor.PrincipalId, conversationName, out var instance))
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

                // Strangle: tip journal still keys by the principal-partitioned chat instance name.
                await SseResponse.WriteAsync(
                    http.Response,
                    JournalProjection.WatchAsync(
                        token => sessionJournal.WatchChatOutgoingAsync(instance, cursor, token),
                        HttpSurfacePaths.ChatTurnEvent,
                        ChatStreamsHttpMaps.ProjectTurn,
                        cancellationToken),
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
}
