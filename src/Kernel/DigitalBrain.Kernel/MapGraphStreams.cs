using System.Net.ServerSentEvents;
using DigitalBrain.Abstractions;
using DigitalBrain.Auth;

namespace DigitalBrain.Kernel;

internal static class GraphStreamsHttpMaps
{
    public static IEndpointRouteBuilder MapGraphStreams(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            HttpSurfacePaths.GraphEventsPath,
            static async Task (
                HttpContext http,
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

                var cursor = afterSequence.GetValueOrDefault();
                if (cursor < 0)
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                await SseResponse.WriteAsync(
                    http.Response,
                    WatchGraphChangesAsync(sessionJournal, actor.PrincipalId, cursor, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            });

        return endpoints;
    }

    private static IAsyncEnumerable<SseItem<GraphEvent>> WatchGraphChangesAsync(
        OwnerSessionJournal sessionJournal,
        PrincipalId principal,
        long afterSequence,
        CancellationToken cancellationToken)
        => JournalProjection.WatchAsync(
            token => sessionJournal.WatchGraphOutgoingAsync(principal, afterSequence, token),
            HttpSurfacePaths.GraphChangeEvent,
            ProjectChange,
            cancellationToken);

    private static GraphEvent? ProjectChange(SynapseDelivery delivery)
        => delivery.Synapse switch
        {
            Connected live => new GraphEvent(
                delivery.Sequence,
                "connected",
                live.ConnectionId,
                live.Source.ToString(),
                live.SynapseAlias,
                live.Target.ToString(),
                delivery.Timestamp),
            Disconnected gone => new GraphEvent(
                delivery.Sequence,
                "disconnected",
                gone.ConnectionId,
                null,
                null,
                null,
                delivery.Timestamp),
            _ => null,
        };
}
