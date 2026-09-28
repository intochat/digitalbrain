using DigitalBrain.AI;
using DigitalBrain.Core.Enforcement;
using IntoChat.Workspace;

namespace IntoChat.Agent;

internal static class VoiceEndpoints
{
    internal const int MaxAudioBytes = 4 * 1024 * 1024;
    internal sealed record VoiceInput(string? Audio);

    public static void MapWorkspaceVoice(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/workspaces/{workspaceId}/voice", async (
            string workspaceId, VoiceInput input, HttpContext http,
            CancellationToken ct) =>
        {
            if (!AgentEndpoints.ValidId(workspaceId)) { return Results.BadRequest(); }
            return await Transcribe(input, http.RequestServices.GetService<IAudioTranscriptionService>(), ct);
        }).AddEndpointFilter(WorkspaceAccessFilter.EnforceAsync).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(6 * 1024 * 1024));
    }

    internal static async Task<IResult> Transcribe(VoiceInput input, IAudioTranscriptionService? transcription, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(input.Audio) || input.Audio.Length > (MaxAudioBytes + 2) / 3 * 4)
        { return Results.BadRequest(new { error = "Record between 1 and 120 seconds of audio." }); }
        byte[] bytes;
        try { bytes = Convert.FromBase64String(input.Audio); }
        catch (FormatException) { return Results.BadRequest(new { error = "Invalid audio encoding." }); }
        if (bytes.Length < 44 || bytes.Length > MaxAudioBytes ||
            !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        { return Results.BadRequest(new { error = "A WAV recording is required." }); }
        if (transcription?.IsReady != true)
        { return Results.Json(new { error = "Voice transcription is unavailable. Please try again later." }, statusCode: 503); }
        try
        {
            using var audio = new MemoryStream(bytes, writable: false);
            var text = await transcription.TranscribeAsync(audio, "voice.wav", ct);
            return Results.Ok(new { text });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        { return Results.Json(new { error = "Voice transcription failed. Please try again." }, statusCode: 502); }
    }
}
