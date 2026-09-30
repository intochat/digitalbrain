namespace DigitalBrain.AI;

// Stands in whenever no transcription model is usable, so a voice
// misconfiguration answers 503 with a reason instead of refusing to boot the
// silo. Every other surface keeps working.
public sealed class UnavailableTranscriptionService : IAudioTranscriptionService
{
    private const string NotConfigured =
        "Voice-to-text is not configured. Call AIModule.WithVoiceToText<T>() in AppHost.";

    public UnavailableTranscriptionService()
        : this(NotConfigured)
    {
    }

    public UnavailableTranscriptionService(string errorMessage)
        => ErrorMessage = errorMessage;

    public bool IsReady => false;
    public bool InitializationFailed => true;
    public string? ErrorMessage { get; }
    public string ModelId => "none";

    public Task<string> TranscribeAsync(string audioFilePath, CancellationToken cancellationToken = default)
        => Task.FromException<string>(new InvalidOperationException(ErrorMessage));

    public Task<string> TranscribeAsync(
        Stream audioStream,
        string fileName,
        CancellationToken cancellationToken = default)
        => Task.FromException<string>(new InvalidOperationException(ErrorMessage));
}
