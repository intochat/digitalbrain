using System.Net.ServerSentEvents;
using DigitalBrain.Abstractions;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal static class SurfaceStreamsHttpMaps
{
    public static IEndpointRouteBuilder MapSurfaceStreams(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            HttpSurfacePaths.SurfaceEventsPath,
            static async Task (
                HttpContext http,
                string surfaceName,
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

                if (string.IsNullOrWhiteSpace(surfaceName)
                    || !TryPrincipalResource(actor.PrincipalId, surfaceName, out var surfaceInstance))
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
                    WatchSurfaceOpenedAsync(sessionJournal, surfaceInstance, cursor, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            });

        return endpoints;
    }

    private static bool TryPrincipalResource(PrincipalId principal, string localName, out string instanceName)
    {
        try
        {
            instanceName = PrincipalSurface.InstanceName(principal, localName);
            return true;
        }
        catch (ArgumentException)
        {
            instanceName = "";
            return false;
        }
    }

    private static IAsyncEnumerable<SseItem<SurfaceOpenedEvent>> WatchSurfaceOpenedAsync(
        OwnerSessionJournal sessionJournal,
        string surfaceName,
        long afterSequence,
        CancellationToken cancellationToken)
        => JournalProjection.WatchAsync(
            token => sessionJournal.WatchSurfaceOutgoingAsync(surfaceName, afterSequence, token),
            HttpSurfacePaths.SurfaceOpenedEvent,
            ProjectSurfaceOpened,
            cancellationToken);

    private static SurfaceOpenedEvent? ProjectSurfaceOpened(SynapseDelivery delivery)
        => delivery.Synapse is not SurfaceOpened opened
            ? null
            : new SurfaceOpenedEvent(
                delivery.Sequence,
                opened.SurfaceKey,
                opened.Title,
                opened.CommandId.ToString(),
                opened.Surface.ToString());
}
