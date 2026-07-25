using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Flutter;
using Orleans;

namespace DigitalBrain.Ui;

internal static class UiEndpoints
{
    public static IEndpointRouteBuilder MapUi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(
            "/shells/{shellName}/scenes",
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

                var shell = brain.Get<IShell>(shellName);
                await shell.Open(new OpenScene(
                    CommandId.New(),
                    request.SceneKey,
                    request.Title));

                return Results.Accepted();
            });

        endpoints.MapGet(
            "/shells/{shellName}/events",
            static async Task (
                HttpContext http,
                string shellName,
                long? afterSequence,
                IDigitalBrain brain,
                IGrainFactory grains,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(shellName);
                ArgumentNullException.ThrowIfNull(http);
                ArgumentNullException.ThrowIfNull(brain);
                ArgumentNullException.ThrowIfNull(grains);
                cancellationToken.ThrowIfCancellationRequested();

                var cursor = afterSequence.GetValueOrDefault();
                if (cursor < 0)
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                http.Response.Headers.CacheControl = "no-cache";
                http.Response.ContentType = "text/event-stream";
                await ShellEventFeed.WriteSceneOpenedSseAsync(
                    http.Response.Body,
                    grains,
                    brain,
                    shellName,
                    cursor,
                    cancellationToken);
            });

        endpoints.MapPost(
            "/scenes/{sceneName}/controls/{controlId}/activate",
            static async Task<IResult> (
                string sceneName,
                string controlId,
                ActivateControlRequest request,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(sceneName);
                ArgumentException.ThrowIfNullOrWhiteSpace(controlId);
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(brain);
                ArgumentException.ThrowIfNullOrWhiteSpace(request.Intent);
                cancellationToken.ThrowIfCancellationRequested();

                var sceneKey = string.IsNullOrWhiteSpace(request.SceneKey)
                    ? sceneName
                    : request.SceneKey;

                await brain.SendAsync<IScene>(
                    sceneName,
                    new ControlActivated(sceneKey, controlId, request.Intent));

                return Results.Accepted();
            });

        return endpoints;
    }
}

internal sealed record OpenSceneRequest(string SceneKey, string Title);

internal sealed record ActivateControlRequest(string Intent, string? SceneKey = null);
