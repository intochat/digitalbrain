namespace DigitalBrain.AI;

// Testing-mode counterpart of the real transcription services: deterministic and
// offline. Without it, a suite in testing mode with a hosted model pinned would
// bill real provider calls on every voice upload — the local Foundry path used to
// make that impossible by construction.
internal sealed class TestTranscriptionService : IAudioTranscriptionService
{
    internal const string Transcript = "test transcription";

    public bool IsReady => true;
    public bool InitializationFailed => false;
    public string? ErrorMessage => null;
    public string ModelId => "test-transcription";

    public Task<string> TranscribeAsync(string audioFilePath, CancellationToken cancellationToken = default)
        => Task.FromResult(Transcript);

    public Task<string> TranscribeAsync(
        Stream audioStream,
        string fileName,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Transcript);
}
