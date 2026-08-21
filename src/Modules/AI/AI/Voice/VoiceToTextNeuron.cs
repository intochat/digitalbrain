using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.AI;

[GrainType(IVoiceToText.GrainTypeName)]
public sealed class VoiceToTextNeuron : Neuron, IVoiceToText
{
    public const int MaxAudioBytes = 12 * 1024 * 1024; // ~12 MB hard cap

    private readonly IAudioTranscriptionService _transcription;

    public VoiceToTextNeuron()
    {
        _transcription = ServiceProvider.GetRequiredService<IAudioTranscriptionService>();
    }

    public async Task HandleAsync(TranscribeAudio synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (synapse.CommandId.Value == Guid.Empty)
        {
            throw new NeuronAuthorizationException("TranscribeAudio requires a command id.");
        }

        if (synapse.Audio is not { Length: > 0 })
        {
            throw new NeuronAuthorizationException("TranscribeAudio requires non-empty audio.");
        }

        if (synapse.Audio.Length > MaxAudioBytes)
        {
            throw new NeuronAuthorizationException(
                $"Audio exceeds the {MaxAudioBytes} byte limit.");
        }

        if (!_transcription.IsReady)
        {
            throw new NeuronAuthorizationException(
                _transcription.ErrorMessage
                ?? "Whisper is not ready yet. Retry after the model finishes loading.");
        }

        await using var stream = new MemoryStream(synapse.Audio, writable: false);
        var fileName = string.IsNullOrWhiteSpace(synapse.FileName) ? "voice.wav" : synapse.FileName.Trim();
        var text = await _transcription
            .TranscribeAsync(stream, fileName, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new NeuronAuthorizationException("Transcription produced empty text.");
        }

        await ReplyAsync(
                new Transcribed(synapse.CommandId, text.Trim(), _transcription.ModelId),
                cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }
}
