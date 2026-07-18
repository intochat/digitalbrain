namespace Ino.Telegram.Host.Services;

/// <summary>
/// Local replacement for the legacy <c>Core.AI.IAudioTranscriptionService</c>
/// — same shape, kept inside the bot project so the Telegram host doesn't
/// depend on a Whisper abstraction in the kernel. The current sole
/// implementation is <see cref="FoundryLocalTranscriptionService"/>; a future
/// slice can introduce alternatives (cloud Whisper, etc.) by adding more
/// implementations and selecting via configuration.
/// </summary>
public interface IAudioTranscriptionService
{
    Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct = default);
    Task<string> TranscribeAsync(Stream audioStream, string fileName, CancellationToken ct = default);
}

/// <summary>Decoder for Telegram's voice payload format (Opus-in-Ogg) into WAV.</summary>
public interface IAudioConverter
{
    string ConvertToWav(string inputPath);
}

/// <summary>
/// Probe surface so a /health or /readiness endpoint can report whether the
/// Whisper model finished downloading + loading. Boot can take minutes on a
/// fresh install — without this signal the bot would silently swallow voice
/// messages until init completes.
/// </summary>
public interface IWhisperReadiness
{
    bool IsReady { get; }
    bool InitializationFailed { get; }
    string? ErrorMessage { get; }
}
