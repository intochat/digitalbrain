namespace DigitalBrain.AI;

// Fail-closed STT placeholder when Whisper is disabled or no model was projected.
public sealed class UnavailableTranscriptionService : IAudioTranscriptionService
{
    public const string DisabledMessage =
        "Voice-to-text is disabled (DigitalBrain:AI:Whisper:Enabled=false).";

    public const string NotConfiguredMessage =
        "Voice-to-text is off — no Whisper model is configured for this host "
        + "(AppHost skipped WithVoiceToText / EnableVoiceToText=false, or ModelId unset).";

    private readonly string _errorMessage;

    public UnavailableTranscriptionService()
        : this(NotConfiguredMessage)
    {
    }

    public UnavailableTranscriptionService(string errorMessage)
    {
        _errorMessage = string.IsNullOrWhiteSpace(errorMessage)
            ? NotConfiguredMessage
            : errorMessage.Trim();
    }

    public bool IsReady => false;
    public bool InitializationFailed => true;
    public string? ErrorMessage => _errorMessage;
    public string ModelId => "none";

    public Task<string> TranscribeAsync(string audioFilePath, CancellationToken cancellationToken = default)
        => Task.FromException<string>(new InvalidOperationException(ErrorMessage));

    public Task<string> TranscribeAsync(
        Stream audioStream,
        string fileName,
        CancellationToken cancellationToken = default)
        => Task.FromException<string>(new InvalidOperationException(ErrorMessage));
}
