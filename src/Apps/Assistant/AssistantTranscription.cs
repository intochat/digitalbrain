using DigitalBrain.AI;

namespace DigitalBrain.Apps.Assistant;

[GenerateSerializer, Alias("assistant.transcription-result")]
public sealed record AssistantTranscriptionResult(
    [property: Id(0)] int Status,
    [property: Id(1)] string? Text = null,
    [property: Id(2)] string? Error = null);

public sealed class AssistantTranscription(IAudioTranscriptionService? transcription)
{
    public const int MaxAudioBytes = 4 * 1024 * 1024;

    public async Task<AssistantTranscriptionResult> Transcribe(string? base64, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(base64) || base64.Length > (MaxAudioBytes + 2) / 3 * 4)
        { return new(400, Error: "Record between 1 and 120 seconds of audio."); }
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch (FormatException) { return new(400, Error: "Invalid audio encoding."); }
        if (bytes.Length < 44 || bytes.Length > MaxAudioBytes ||
            !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        { return new(400, Error: "A WAV recording is required."); }
        if (transcription?.IsReady != true)
        { return new(503, Error: "Voice transcription is unavailable. Please try again later."); }
        try
        {
            using var audio = new MemoryStream(bytes, writable: false);
            var text = await transcription.TranscribeAsync(audio, "voice.wav", ct);
            return new(200, Text: text);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        { return new(502, Error: "Voice transcription failed. Please try again."); }
    }
}
