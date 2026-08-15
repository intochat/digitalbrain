namespace DigitalBrain.AI;

// Placeholder when WithVoiceToText was not enabled in AppHost.
public sealed class UnavailableTranscriptionService : IAudioTranscriptionService
{
    public bool IsReady => false;
    public bool InitializationFailed => true;
    public string? ErrorMessage => "Voice-to-text is not configured. Call AIModule.WithVoiceToText<T>() in AppHost.";
    public string ModelId => "none";

    public Task<string> TranscribeAsync(string audioFilePath, CancellationToken cancellationToken = default)
        => Task.FromException<string>(new InvalidOperationException(ErrorMessage));

    public Task<string> TranscribeAsync(
        Stream audioStream,
        string fileName,
        CancellationToken cancellationToken = default)
        => Task.FromException<string>(new InvalidOperationException(ErrorMessage));
}
