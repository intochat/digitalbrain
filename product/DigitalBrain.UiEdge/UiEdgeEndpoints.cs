using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Shell;

namespace DigitalBrain.UiEdge;

internal static class UiEdgeEndpoints
{
    public static IEndpointRouteBuilder MapUI(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(
            UiEdgeContract.OpenScenePath,
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
                    cancellationToken);

                return Results.Accepted();
            });

        endpoints.MapGet(
            UiEdgeContract.ShellEventsPath,
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
                    ShellEventFeed.WatchSceneOpenedAsync(sessionJournal, shellName, cursor, cancellationToken),
                    cancellationToken);
            });

        endpoints.MapPost(
            UiEdgeContract.ActivateControlPath,
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
                    cancellationToken);

                return Results.Accepted();
            });

        return endpoints;
    }
}
