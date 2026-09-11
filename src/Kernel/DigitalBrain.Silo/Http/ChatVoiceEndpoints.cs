using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal static class ChatVoiceEndpoints
{
    private const long MaxUploadBytes = 25 * 1024 * 1024;

    public static IEndpointRouteBuilder MapChatVoiceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/chats/{chatName}/voice",
            static async Task<IResult> (string chatName, HttpContext http, IGrainFactory grains,
                IAudioTranscriptionService transcription, CancellationToken cancellationToken) =>
            {
                if (!transcription.IsReady)
                {
                    return Results.Json(new
                    {
                        error = transcription.ErrorMessage ?? "Whisper is not ready. Retry after the model finishes loading.",
                    }, statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                if (!http.Request.HasFormContentType)
                {
                    return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }

                IFormCollection form;
                try
                {
                    form = await http.Request.ReadFormAsync(cancellationToken);
                }
                catch (BadHttpRequestException error) when (error.StatusCode == StatusCodes.Status413PayloadTooLarge)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }
                catch (InvalidDataException)
                {
                    return Results.BadRequest();
                }

                var file = form.Files.GetFile("audio");
                if (file is null || file.Length <= 0)
                {
                    return Results.BadRequest();
                }

                if (file.Length > MaxUploadBytes)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }

                var fileName = string.IsNullOrWhiteSpace(file.FileName) ? "voice.wav" : file.FileName;
                string text;
                try
                {
                    await using var stream = file.OpenReadStream();
                    text = await transcription.TranscribeAsync(stream, fileName, cancellationToken);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    return Results.UnprocessableEntity(new { error = error.Message });
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    return Results.UnprocessableEntity(new { error = "Transcription produced empty text." });
                }

                try
                {
                    var chat = grains.GetGrain<IChat>(new NeuronId(UIVocabulary.ChatType, chatName).ToGrainId());
                    var accepted = await chat.Send(new SendMessage(CommandId.New(), text.Trim()), cancellationToken);
                    return Results.Accepted(value: accepted);
                }
                catch (Exception error) when (EdgeResults.IsRejection(error))
                {
                    return EdgeResults.Rejected(error);
                }
            }).AddEndpointFilter(new NeuronNameFilter("chatName"));

        return endpoints;
    }
}
