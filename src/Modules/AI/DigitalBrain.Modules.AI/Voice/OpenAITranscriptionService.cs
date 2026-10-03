using System.ClientModel;
using DigitalBrain.Platform.Contracts.Integrations;
using OpenAI;
using OpenAI.Audio;

namespace DigitalBrain.AI;

// Hosted speech-to-text over the provider's own audio endpoint. Deliberately not
// an IHostedService: there is no model to download or load, which is the whole
// reason this exists alongside the Foundry Local path.
public sealed class OpenAITranscriptionService : IAudioTranscriptionService
{
    // A 25 MB upload is roughly 13 minutes of audio; the default timeout is far
    // too short for the far end to finish transcribing it.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

    // The provider infers the container from the filename, and rejects anything
    // it does not recognise.
    private static readonly string[] AcceptedExtensions =
        [".flac", ".m4a", ".mp3", ".mp4", ".mpeg", ".mpga", ".oga", ".ogg", ".wav", ".webm"];

    private const string FallbackFileName = "voice.wav";
    private const string OpusExtension = ".opus";

    private readonly TranscriptionModel _model;
    private readonly IAiCredentials _credentials;

    internal OpenAITranscriptionService(TranscriptionModel model, IAiCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(credentials);

        _model = model;
        _credentials = credentials;
    }

    private string Integration => AiIntegrations.IdOf(_model.Provider);

    // Not ready reports rather than throws: voice answers 503 with the reason and the rest of the
    // process is unaffected.
    public bool IsReady => _model.Formats.HasFlag(TranscriptionFormats.Text) && _credentials.IsReady(Integration);

    public bool InitializationFailed => !IsReady;

    public string? ErrorMessage
    {
        get
        {
            if (!_model.Formats.HasFlag(TranscriptionFormats.Text))
            {
                return $"{_model.DisplayName} does not offer a plain-text response, which voice transcription requires.";
            }

            var status = _credentials.StatusOf(Integration);
            return status.Status == RegistrationStatus.Ready
                ? null
                : $"{_model.DisplayName} requires the {Integration} integration, which is {status.Status}.";
        }
    }

    public string ModelId => _model.Id;

    public async Task<string> TranscribeAsync(
        string audioFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFilePath);

        var stream = File.OpenRead(audioFilePath);
        await using (stream.ConfigureAwait(false))
        {
            return await TranscribeAsync(stream, Path.GetFileName(audioFilePath), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<string> TranscribeAsync(
        Stream audioStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioStream);

        if (!IsReady)
        {
            throw new InvalidOperationException(ErrorMessage ?? "Transcription is not configured.");
        }

        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);

        // Asserted against the catalogue rather than assumed: the gpt-4o transcribe
        // models reject the verbose and subtitle formats outright.
        var options = new AudioTranscriptionOptions { ResponseFormat = AudioTranscriptionFormat.Text };

        var transcription = await client
            .TranscribeAudioAsync(audioStream, NormalizeFileName(fileName), options, cancellationToken)
            .ConfigureAwait(false);

        return transcription.Value.Text ?? string.Empty;
    }

    private async Task<AudioClient> CreateClientAsync(CancellationToken cancellationToken)
    {
        var apiKey = await _credentials.ReleaseSecretAsync(Integration, AiIntegrations.ApiKeyField, cancellationToken).ConfigureAwait(false);
        var options = new OpenAIClientOptions { NetworkTimeout = RequestTimeout };
        if (_credentials.Setting(Integration, AiIntegrations.EndpointField) is { Length: > 0 } endpoint)
        {
            options.Endpoint = new Uri(endpoint);
        }

        return new AudioClient(_model.Id, new ApiKeyCredential(apiKey), options);
    }

    // An unrecognised extension is a 400 from the provider, which would surface as
    // an opaque transcription failure. The shell always sends WAV.
    internal static string NormalizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return FallbackFileName;
        }

        var extension = Path.GetExtension(fileName);
        if (AcceptedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return fileName;
        }

        // Opus rides in an Ogg container, which the provider does accept, but it is
        // not in its extension list. Relabelling it .wav like any other unknown
        // extension would hand it Ogg bytes announced as WAV; the Foundry path
        // already treats .opus as an Ogg container, so these uploads are real.
        if (OpusExtension.Equals(extension, StringComparison.OrdinalIgnoreCase))
        {
            return Path.ChangeExtension(fileName, ".ogg");
        }

        return FallbackFileName;
    }
}
