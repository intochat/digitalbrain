using System.ClientModel;
using Microsoft.Extensions.Configuration;
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

    private readonly TranscriptionModel _model;
    private readonly Lazy<AudioClient>? _client;

    public OpenAITranscriptionService(TranscriptionModel model, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(configuration);

        _model = model;

        // Every failure below leaves _client null, which reports as "not ready"
        // rather than throwing: voice answers 503 with the reason and the rest of
        // the process is unaffected.
        if (!model.Formats.HasFlag(TranscriptionFormats.Text))
        {
            ErrorMessage =
                $"{model.DisplayName} does not offer a plain-text response, which voice transcription requires.";
            return;
        }

        var apiKeyKey = $"{AIClients.ConfigurationRoot}:{model.Provider}:ApiKey";
        if (configuration[apiKeyKey] is not { Length: > 0 } apiKey)
        {
            ErrorMessage = $"{model.DisplayName} requires {apiKeyKey}.";
            return;
        }

        var options = new OpenAIClientOptions { NetworkTimeout = RequestTimeout };
        if (configuration[$"{AIClients.ConfigurationRoot}:{model.Provider}:Endpoint"] is { Length: > 0 } endpoint)
        {
            options.Endpoint = new Uri(endpoint);
        }

        _client = new Lazy<AudioClient>(
            () => new AudioClient(model.Id, new ApiKeyCredential(apiKey), options));
    }

    public bool IsReady => _client is not null;

    public bool InitializationFailed => _client is null;

    public string? ErrorMessage { get; }

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

        var client = _client?.Value
            ?? throw new InvalidOperationException(ErrorMessage ?? "Transcription is not configured.");

        // Asserted against the catalogue rather than assumed: the gpt-4o transcribe
        // models reject the verbose and subtitle formats outright.
        var options = new AudioTranscriptionOptions { ResponseFormat = AudioTranscriptionFormat.Text };

        var transcription = await client
            .TranscribeAudioAsync(audioStream, NormalizeFileName(fileName), options, cancellationToken)
            .ConfigureAwait(false);

        return transcription.Value.Text ?? string.Empty;
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
        return AcceptedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            ? fileName
            : FallbackFileName;
    }
}
