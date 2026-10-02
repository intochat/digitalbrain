using DigitalBrain.AI.Media;
using DigitalBrain.AI;

namespace DigitalBrain.Assistant;


public sealed class AssistantTranscription(ISpeechRecognizer? transcription)
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
        if (transcription is null || !(await transcription.Describe()).Available)
        { return new(503, Error: "Voice transcription is unavailable. Please try again later."); }
        try
        {
            var result = await transcription.Recognize(new(new(bytes, "audio/wav"), "voice.wav"), ct);
            var text = result.Text;
            return new(200, Text: text);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        { return new(502, Error: "Voice transcription failed. Please try again."); }
    }
}
