using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Kernel;

internal static class OwnerCommandsHttpMaps
{
    internal static readonly TimeSpan TurnBudget =
        TimeSpan.Parse(NeuronCallTimeouts.LongRunning, System.Globalization.CultureInfo.InvariantCulture);

    public static IEndpointRouteBuilder MapOwnerCommands(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(
            HttpSurfacePaths.OwnerCommandsPath,
            static async Task (
                HttpContext http,
                OwnerCommandRequest request,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(http);
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                if (!HttpActor.TryGet(http, out var actor))
                {
                    http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                if (string.IsNullOrWhiteSpace(request.Kind))
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                if (string.Equals(request.Kind, HttpSurfacePaths.KindChatSend, StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(request.ChatName) || string.IsNullOrWhiteSpace(request.Text))
                    {
                        http.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    if (!TryPrincipalResource(actor.PrincipalId, request.ChatName, out var chatInstance))
                    {
                        http.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    await SseResponse.WriteAsync(
                        http.Response,
                        StreamDeltasAsync(brain, chatInstance, request.Text, actor, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(request.Kind, HttpSurfacePaths.KindChatButton, StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(request.ChatName)
                        || string.IsNullOrWhiteSpace(request.ButtonId)
                        || string.IsNullOrWhiteSpace(request.Action)
                        || !TryParseCommandId(request.OfferCommandId, out var offerCommandId))
                    {
                        http.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    if (!TryPrincipalResource(actor.PrincipalId, request.ChatName, out var chatInstance))
                    {
                        http.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    await brain.FireAsync<IButton>(
                        ChatButtons.OfferedInstanceName(chatInstance, offerCommandId, request.ButtonId),
                        new ButtonClicked(offerCommandId, request.ButtonId, request.Action),
                        cancellationToken).ConfigureAwait(false);
                    http.Response.StatusCode = StatusCodes.Status202Accepted;
                    return;
                }

                if (string.Equals(request.Kind, HttpSurfacePaths.KindSurfaceOpen, StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(request.SurfaceName)
                        || string.IsNullOrWhiteSpace(request.SurfaceKey)
                        || string.IsNullOrWhiteSpace(request.Title))
                    {
                        http.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    string surfaceInstance;
                    try
                    {
                        surfaceInstance = PrincipalSurface.InstanceName(actor.PrincipalId, request.SurfaceName);
                    }
                    catch (ArgumentException)
                    {
                        http.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    await brain.FireAsync<ISurface>(
                        surfaceInstance,
                        new OpenSurface(CommandId.New(), request.SurfaceKey, request.Title),
                        cancellationToken).ConfigureAwait(false);
                    http.Response.StatusCode = StatusCodes.Status202Accepted;
                    return;
                }

                http.Response.StatusCode = StatusCodes.Status400BadRequest;
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

    private static bool TryParseCommandId(string? value, out CommandId commandId)
    {
        commandId = default;
        if (string.IsNullOrWhiteSpace(value) || !Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            return false;
        }

        commandId = new CommandId(id);
        return true;
    }

    private static async IAsyncEnumerable<SseItem<ChatResponseUpdate>> StreamDeltasAsync(
        IDigitalBrain brain,
        string chatInstance,
        string text,
        ActorContext actor,
        [EnumeratorCancellation] CancellationToken requestAborted)
    {
        using var turn = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        turn.CancelAfter(TurnBudget);

        var command = CommandId.New();
        await foreach (var chunk in brain.GetGrainProxy<IChat>(chatInstance)
            .SendStreaming(new SendMessage(command, text, actor), turn.Token)
            .ConfigureAwait(false))
        {
            turn.Token.ThrowIfCancellationRequested();
            yield return new SseItem<ChatResponseUpdate>(chunk, HttpSurfacePaths.ChatDeltaEvent);
        }
    }
}
