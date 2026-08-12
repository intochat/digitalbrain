using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Auth;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using DigitalBrain.Conversations;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Kernel;

internal static class ConversationMessagesHttpMaps
{
    public static IEndpointRouteBuilder MapConversationMessages(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(
            HttpSurfacePaths.ConversationMessagesStreamPath,
            static async Task (
                HttpContext http,
                string conversationName,
                ConversationSendRequest? body,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(http);
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                if (!HttpActor.TryGet(http, out var actor))
                {
                    http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                var text = body?.Text;
                if (string.IsNullOrWhiteSpace(conversationName)
                    || string.IsNullOrWhiteSpace(text)
                    || !TryPrincipalResource(actor.PrincipalId, conversationName, out var instance))
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                await SseResponse.WriteAsync(
                    http.Response,
                    StreamDeltasAsync(brain, instance, text.Trim(), actor, cancellationToken),
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

    private static async IAsyncEnumerable<SseItem<ChatResponseUpdate>> StreamDeltasAsync(
        IDigitalBrain brain,
        string conversationInstance,
        string text,
        ActorContext actor,
        [EnumeratorCancellation] CancellationToken requestAborted)
    {
        using var budget = new CancellationTokenSource(OwnerCommandsHttpMaps.TurnBudget);
        var command = CommandId.New();
        var conversation = brain.GetGrainProxy<IConversation>(conversationInstance);
        var accepted = await conversation
            .Send(new SendConversationMessage(command, text, actor))
            .ConfigureAwait(false);

        using var observer = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, budget.Token);
        // Strangle: durable Responded/TurnLifecycle still emit from tip IChat journal.
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
}

internal sealed record ConversationSendRequest(string? Text);
