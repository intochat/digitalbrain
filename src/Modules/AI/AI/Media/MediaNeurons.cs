using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.AI.Media;

public abstract class MediaNeuron : Neuron
{
    public const int MaxPayloadBytes = 8 * 1024 * 1024;
    protected static MediaCapabilities Capabilities(MediaOperation operation, Func<bool> available)
    {
        try { return new(operation, available(), MaxPayloadBytes); }
        catch (InvalidOperationException) { return new(operation, false, MaxPayloadBytes, "Provider configuration is unavailable."); }
    }
    internal static void ValidateText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (text.Length > 32_768) { throw new ArgumentException("Text must not exceed 32768 characters."); }
    }
    internal static void ValidatePayload(MediaPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(payload.Content);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload.MediaType);
        if (payload.Content.Length is 0 or > MaxPayloadBytes) { throw new ArgumentException("Media must contain between 1 byte and 8 MiB."); }
    }
    protected async Task<T> Execute<T>(MediaOperation operation, CancellationToken ct,
        Func<Guid, Task<T>> action, Func<T, Signal> completed)
    {
        var id = Guid.NewGuid();
        T result;
        try { ct.ThrowIfCancellationRequested(); result = await action(id); ct.ThrowIfCancellationRequested(); }
        catch (Exception error)
        {
            await PublishAsync(new MediaOperationFailed(id, operation,
                error is OperationCanceledException ? "cancelled" : error is NotSupportedException ? "unavailable" : "failed"));
            throw;
        }
        await PublishAsync(completed(result));
        return result;
    }
}

public sealed class ImageGeneratorNeuron(IServiceProvider services) : MediaNeuron, IImageGenerator
{
    public Task<MediaCapabilities> Describe() => Task.FromResult(Capabilities(MediaOperation.ImageGeneration,
        () => services.GetService<IImageGeneration>() is not null));
    public Task<ImageGenerationResult> Generate(ImageGenerationRequest request, CancellationToken cancellationToken = default)
        => Execute(MediaOperation.ImageGeneration, cancellationToken, async id =>
        {
            ArgumentNullException.ThrowIfNull(request); ValidateText(request.Prompt);
            var service = services.GetService<IImageGeneration>() ?? throw new NotSupportedException("Configure an image generation provider.");
            var result = await service.GenerateAsync(request.Prompt, cancellationToken);
            var payload = new MediaPayload(result.Content, result.MediaType); ValidatePayload(payload);
            return new ImageGenerationResult(id, payload, result.Model);
        }, result => new ImageGenerated(result.OperationId, result.Model));
}

public sealed class EmbeddingNeuron(IServiceProvider services) : MediaNeuron, IEmbeddingModel
{
    public Task<MediaCapabilities> Describe() => Task.FromResult(Capabilities(MediaOperation.Embedding,
        () => services.GetService<IEmbeddingGenerator<string, Embedding<float>>>() is not null) with
    { MaxBatchItems = 128 });
    public Task<EmbeddingResult> Embed(EmbeddingRequest request, CancellationToken cancellationToken = default)
        => Execute(MediaOperation.Embedding, cancellationToken, async id =>
        {
            ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(request.Inputs);
            if (request.Inputs.Length is 0 or > 128) { throw new ArgumentException("Supply between 1 and 128 inputs."); }
            foreach (var text in request.Inputs) { ValidateText(text); }
            var service = services.GetService<IEmbeddingGenerator<string, Embedding<float>>>()
                ?? throw new NotSupportedException("Configure an embedding provider.");
            var generated = await service.GenerateAsync(request.Inputs, cancellationToken: cancellationToken);
            if (generated.Count != request.Inputs.Length) { throw new InvalidOperationException("Provider returned the wrong number of embeddings."); }
            if (generated.Sum(x => (long)x.Vector.Length * sizeof(float)) > MaxPayloadBytes) { throw new InvalidOperationException("Embedding response exceeds 8 MiB."); }
            return new EmbeddingResult(id, generated.Select(x => x.Vector.ToArray()).ToArray());
        }, result => new EmbeddingsGenerated(result.OperationId, result.Vectors.Length));
}

public sealed class SpeechRecognizerNeuron(IServiceProvider services) : MediaNeuron, ISpeechRecognizer
{
    public Task<MediaCapabilities> Describe()
    {
        var service = services.GetService<IAudioTranscriptionService>();
        return Task.FromResult(new MediaCapabilities(MediaOperation.SpeechRecognition, service?.IsReady == true,
            MaxPayloadBytes, service?.ErrorMessage));
    }
    public Task<SpeechRecognitionResult> Recognize(SpeechRecognitionRequest request, CancellationToken cancellationToken = default)
        => Execute(MediaOperation.SpeechRecognition, cancellationToken, async id =>
        {
            ArgumentNullException.ThrowIfNull(request); ValidatePayload(request.Audio);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
            if (request.FileName.Length > 255 || request.FileName.IndexOfAny(['/', '\\']) >= 0) { throw new ArgumentException("Supply a filename without a path."); }
            var service = services.GetService<IAudioTranscriptionService>();
            if (service?.IsReady != true) { throw new NotSupportedException("Speech recognition is unavailable."); }
            using var stream = new MemoryStream(request.Audio.Content, writable: false);
            var text = await service.TranscribeAsync(stream, request.FileName, cancellationToken);
            if (text.Length > 1_000_000) { throw new InvalidOperationException("Transcription exceeds the response limit."); }
            return new SpeechRecognitionResult(id, text, service.ModelId);
        }, result => new SpeechRecognized(result.OperationId, result.Model));
}

public sealed class SpeechSynthesizerNeuron(ISpeechSynthesisTransport transport) : MediaNeuron, ISpeechSynthesizer
{
    public Task<MediaCapabilities> Describe() => Task.FromResult(new MediaCapabilities(MediaOperation.SpeechSynthesis,
        transport.IsAvailable, MaxPayloadBytes, transport.IsAvailable ? null : "Configure a speech synthesis transport."));
    public Task<SpeechSynthesisResult> Synthesize(SpeechSynthesisRequest request, CancellationToken cancellationToken = default)
        => Execute(MediaOperation.SpeechSynthesis, cancellationToken, async id =>
        {
            ArgumentNullException.ThrowIfNull(request); ValidateText(request.Text); ValidateText(request.Voice);
            if (!transport.IsAvailable) { throw new NotSupportedException("Speech synthesis is unavailable."); }
            var audio = await transport.Synthesize(request, cancellationToken); ValidatePayload(audio);
            return new SpeechSynthesisResult(id, audio, transport.Model);
        }, result => new SpeechSynthesized(result.OperationId, result.Model));
}