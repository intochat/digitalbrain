using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Conversations;
using DigitalBrain.Client;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;
using DigitalBrain.Auth;

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

                if (string.Equals(request.Kind, HttpSurfacePaths.KindConversationSend, StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(request.ChatName) || string.IsNullOrWhiteSpace(request.Text))
                    {
                        http.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    if (!TryPrincipalResource(actor.PrincipalId, request.ChatName, out var conversationInstance))
                    {
                        http.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    await SseResponse.WriteAsync(
                        http.Response,
                        StreamConversationDeltasAsync(brain, conversationInstance, request.Text, actor, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
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

                if (string.Equals(request.Kind, HttpSurfacePaths.KindChatCancelTurn, StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(request.ChatName)
                        || !TryParseCommandId(request.CommandId, out var cancelCommandId)
                        || string.IsNullOrWhiteSpace(request.TurnId)
                        || !Guid.TryParse(request.TurnId, out var turnGuid)
                        || turnGuid == Guid.Empty)
                    {
                        http.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    if (!TryPrincipalResource(actor.PrincipalId, request.ChatName, out var chatInstance))
                    {
                        http.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    await brain.GetGrainProxy<IChat>(chatInstance)
                        .Cancel(new CancelTurn(cancelCommandId, new Chat.TurnId(turnGuid), actor))
                        .ConfigureAwait(false);
                    http.Response.StatusCode = StatusCodes.Status202Accepted;
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


    private static async IAsyncEnumerable<SseItem<ChatResponseUpdate>> StreamConversationDeltasAsync(
        IDigitalBrain brain,
        string conversationInstance,
        string text,
        ActorContext actor,
        [EnumeratorCancellation] CancellationToken requestAborted)
    {
        using var budget = new CancellationTokenSource(TurnBudget);
        var command = CommandId.New();
        var accepted = await brain.GetGrainProxy<DigitalBrain.Conversations.IConversation>(conversationInstance)
            .Send(new DigitalBrain.Conversations.SendConversationMessage(command, text, actor))
            .ConfigureAwait(false);

        using var observer = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, budget.Token);
        var chatId = NeuronId.For<IChat>(brain.Owner, conversationInstance);

        await foreach (var page in brain.WatchJournalAsync(
            chatId,
            JournalKind.Outgoing,
            afterSequence: 0,
            observer.Token).ConfigureAwait(false))
        {
            foreach (var delivery in page.Delta)
            {
                if (delivery.Synapse is Responded responded && responded.CommandId == command)
                {
                    yield return new SseItem<ChatResponseUpdate>(
                        new ChatResponseUpdate(ChatRole.Assistant, responded.Text),
                        HttpSurfacePaths.ChatDeltaEvent);
                    yield break;
                }

                if (delivery.Synapse is TurnLifecycle life
                    && life.TurnId.Value == accepted.TurnId.Value
                    && life.Status is ChatTurnStatus.Failed or ChatTurnStatus.Cancelled)
                {
                    yield break;
                }
            }
        }
    }

    // Observer-only SSE: durable Send starts the Execution; request abort detaches this
    // watch without cancelling the turn (P0-2).
    private static async IAsyncEnumerable<SseItem<ChatResponseUpdate>> StreamDeltasAsync(
        IDigitalBrain brain,
        string chatInstance,
        string text,
        ActorContext actor,
        [EnumeratorCancellation] CancellationToken requestAborted)
    {
        using var budget = new CancellationTokenSource(TurnBudget);
        var command = CommandId.New();
        var accepted = await brain.GetGrainProxy<IChat>(chatInstance)
            .Send(new SendMessage(command, text, actor))
            .ConfigureAwait(false);

        // Budget bounds the observer wait; requestAborted only detaches the observer.
        using var observer = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, budget.Token);
        var chatId = NeuronId.For<IChat>(brain.Owner, chatInstance);

        await foreach (var page in brain.WatchJournalAsync(
            chatId,
            JournalKind.Outgoing,
            afterSequence: 0,
            observer.Token).ConfigureAwait(false))
        {
            foreach (var delivery in page.Delta)
            {
                if (delivery.Synapse is Responded responded && responded.CommandId == command)
                {
                    yield return new SseItem<ChatResponseUpdate>(
                        new ChatResponseUpdate(ChatRole.Assistant, responded.Text),
                        HttpSurfacePaths.ChatDeltaEvent);
                    yield break;
                }

                if (delivery.Synapse is TurnLifecycle life
                    && life.TurnId == accepted.TurnId
                    && life.Status is ChatTurnStatus.Failed or ChatTurnStatus.Cancelled)
                {
                    yield break;
                }
            }
        }
    }
}
