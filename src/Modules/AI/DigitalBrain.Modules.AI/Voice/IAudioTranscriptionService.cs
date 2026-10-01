namespace DigitalBrain.AI;

public interface IAudioTranscriptionService
{
    bool IsReady { get; }
    bool InitializationFailed { get; }
    string? ErrorMessage { get; }
    string ModelId { get; }

    Task<string> TranscribeAsync(string audioFilePath, CancellationToken cancellationToken = default);

    Task<string> TranscribeAsync(
        Stream audioStream,
        string fileName,
        CancellationToken cancellationToken = default);
}
