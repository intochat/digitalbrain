using System.Net.ServerSentEvents;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Conversations;
using DigitalBrain.Client;
using Microsoft.Extensions.AI;
using DigitalBrain.Auth;

namespace DigitalBrain.Kernel;

// Upload voice → Whisper → durable conversation turn (Seam 5 /conversations).
internal static class ConversationVoiceHttpMaps
{
    public const long MaxUploadBytes = VoiceToTextNeuron.MaxAudioBytes;

    public static IEndpointRouteBuilder MapConversationVoice(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(
            HttpSurfacePaths.ConversationVoicePath,
            static async Task (
                HttpContext http,
                string conversationName,
                IDigitalBrain brain,
                IAudioTranscriptionService transcription,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(http);
                ArgumentNullException.ThrowIfNull(brain);
                ArgumentNullException.ThrowIfNull(transcription);
                cancellationToken.ThrowIfCancellationRequested();

                if (!HttpActor.TryGet(http, out var actor))
                {
                    http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                if (string.IsNullOrWhiteSpace(conversationName)
                    || !TryPrincipalResource(actor.PrincipalId, conversationName, out var conversationInstance))
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                if (!transcription.IsReady)
                {
                    http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await http.Response.WriteAsJsonAsync(
                        new
                        {
                            error = transcription.ErrorMessage
                                ?? "Whisper is not ready. Retry after the model finishes loading.",
                        },
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!http.Request.HasFormContentType)
                {
                    http.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                    return;
                }

                var form = await http.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
                var file = form.Files.GetFile("audio") ?? form.Files.FirstOrDefault();
                if (file is null || file.Length <= 0)
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                if (file.Length > MaxUploadBytes)
                {
                    http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }

                var fileName = string.IsNullOrWhiteSpace(file.FileName) ? "voice.wav" : file.FileName;
                string text;
                try
                {
                    await using var stream = file.OpenReadStream();
                    text = await transcription
                        .TranscribeAsync(stream, fileName, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    http.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                    await http.Response.WriteAsJsonAsync(
                        new { error = ex.Message },
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    http.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                    await http.Response.WriteAsJsonAsync(
                        new { error = "Transcription produced empty text." },
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                // Same observer SSE path as typed chat.send.
                await SseResponse.WriteAsync(
                    http.Response,
                    StreamDeltasAsync(brain, conversationInstance, text.Trim(), actor, cancellationToken),
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
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken requestAborted)
    {
        using var budget = new CancellationTokenSource(OwnerCommandsHttpMaps.TurnBudget);
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
}
