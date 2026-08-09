using System.Net.ServerSentEvents;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Shell;

namespace DigitalBrain.Kernel;

internal static class ShellStreamsHttpMaps
{
    public static IEndpointRouteBuilder MapShellStreams(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(
            HttpSurfacePaths.OpenScenePath,
            static async Task<IResult> (
                string shellName,
                OpenSceneRequest request,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(shellName);
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(brain);
                ArgumentException.ThrowIfNullOrWhiteSpace(request.SceneKey);
                ArgumentException.ThrowIfNullOrWhiteSpace(request.Title);
                cancellationToken.ThrowIfCancellationRequested();

                await brain.SendAsync<IShell>(
                    shellName,
                    new OpenScene(CommandId.New(), request.SceneKey, request.Title),
                    cancellationToken).ConfigureAwait(false);

                return Results.Accepted();
            });

        endpoints.MapGet(
            HttpSurfacePaths.ShellEventsPath,
            static async Task (
                HttpContext http,
                string shellName,
                long? afterSequence,
                OwnerSessionJournal sessionJournal,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(shellName);
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
                    WatchSceneOpenedAsync(sessionJournal, shellName, cursor, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            });

        endpoints.MapPost(
            HttpSurfacePaths.ActivateControlPath,
            static async Task<IResult> (
                string sceneKey,
                string controlId,
                ActivateControlRequest request,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(sceneKey);
                ArgumentException.ThrowIfNullOrWhiteSpace(controlId);
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(brain);
                ArgumentException.ThrowIfNullOrWhiteSpace(request.Intent);
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(request.SceneKey)
                    && !string.Equals(request.SceneKey, sceneKey, StringComparison.Ordinal))
                {
                    return Results.BadRequest();
                }

                await brain.SendAsync<IScene>(
                    sceneKey,
                    new ControlActivated(sceneKey, controlId, request.Intent),
                    cancellationToken).ConfigureAwait(false);

                return Results.Accepted();
            });

        return endpoints;
    }

    private static IAsyncEnumerable<SseItem<SceneOpenedEvent>> WatchSceneOpenedAsync(
        OwnerSessionJournal sessionJournal,
        string shellName,
        long afterSequence,
        CancellationToken cancellationToken)
        => JournalProjection.WatchAsync(
            token => sessionJournal.WatchShellOutgoingAsync(shellName, afterSequence, token),
            HttpSurfacePaths.SceneOpenedEvent,
            ProjectSceneOpened,
            cancellationToken);

    private static SceneOpenedEvent? ProjectSceneOpened(SynapseDelivery delivery)
        => delivery.Synapse is not SceneOpened opened
            ? null
            : new SceneOpenedEvent(
                delivery.Sequence,
                opened.SceneKey,
                opened.Title,
                opened.CommandId.ToString(),
                opened.Shell.ToString());
}
