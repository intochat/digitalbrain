using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.AI.Media;

public enum MediaOperation { ImageGeneration, Embedding, SpeechRecognition, SpeechSynthesis }

[GenerateSerializer, Alias("ai.media.MediaCapabilities")]
public sealed record MediaCapabilities(
    [property: Id(0)] MediaOperation Operation,
    [property: Id(1)] bool Available,
    [property: Id(2)] int MaxPayloadBytes,
    [property: Id(3)] string? UnavailableReason = null,
    [property: Id(4)] int MaxTextCharacters = 32_768,
    [property: Id(5)] int MaxBatchItems = 1);

/// <summary>Bounded inline media for stage one; no remote URL fetching or filesystem access.</summary>
[GenerateSerializer, Alias("ai.media.MediaPayload")]
public sealed record MediaPayload([property: Id(0)] byte[] Content, [property: Id(1)] string MediaType);
[GenerateSerializer, Alias("ai.media.ImageGenerationRequest")]
public sealed record ImageGenerationRequest([property: Id(0)] string Prompt);
[GenerateSerializer, Alias("ai.media.ImageGenerationResult")]
public sealed record ImageGenerationResult([property: Id(0)] Guid OperationId, [property: Id(1)] MediaPayload Image, [property: Id(2)] string Model);
[GenerateSerializer, Alias("ai.media.EmbeddingRequest")]
public sealed record EmbeddingRequest([property: Id(0)] string[] Inputs);
[GenerateSerializer, Alias("ai.media.EmbeddingResult")]
public sealed record EmbeddingResult([property: Id(0)] Guid OperationId, [property: Id(1)] float[][] Vectors);
[GenerateSerializer, Alias("ai.media.SpeechRecognitionRequest")]
public sealed record SpeechRecognitionRequest([property: Id(0)] MediaPayload Audio, [property: Id(1)] string FileName);
[GenerateSerializer, Alias("ai.media.SpeechRecognitionResult")]
public sealed record SpeechRecognitionResult([property: Id(0)] Guid OperationId, [property: Id(1)] string Text, [property: Id(2)] string Model);
[GenerateSerializer, Alias("ai.media.SpeechSynthesisRequest")]
public sealed record SpeechSynthesisRequest([property: Id(0)] string Text, [property: Id(1)] string Voice);
[GenerateSerializer, Alias("ai.media.SpeechSynthesisResult")]
public sealed record SpeechSynthesisResult([property: Id(0)] Guid OperationId, [property: Id(1)] MediaPayload Audio, [property: Id(2)] string Model);

[Alias("ai.media.IImageGenerator")]
public interface IImageGenerator : INeuron
{
    [ReadOnly] Task<MediaCapabilities> Describe();
    [ResponseTimeout("00:10:00")] Task<ImageGenerationResult> Generate(ImageGenerationRequest request, CancellationToken cancellationToken = default);
}
[Alias("ai.media.IEmbeddingModel")]
public interface IEmbeddingModel : INeuron
{
    [ReadOnly] Task<MediaCapabilities> Describe();
    [ResponseTimeout("00:10:00")] Task<EmbeddingResult> Embed(EmbeddingRequest request, CancellationToken cancellationToken = default);
}
[Alias("ai.media.ISpeechRecognizer")]
public interface ISpeechRecognizer : INeuron
{
    [ReadOnly] Task<MediaCapabilities> Describe();
    [ResponseTimeout("00:10:00")] Task<SpeechRecognitionResult> Recognize(SpeechRecognitionRequest request, CancellationToken cancellationToken = default);
}
[Alias("ai.media.ISpeechSynthesizer")]
public interface ISpeechSynthesizer : INeuron
{
    [ReadOnly] Task<MediaCapabilities> Describe();
    [ResponseTimeout("00:10:00")] Task<SpeechSynthesisResult> Synthesize(SpeechSynthesisRequest request, CancellationToken cancellationToken = default);
}

[GenerateSerializer, Alias("ai.media.ImageGenerated")]
public sealed record ImageGenerated([property: Id(0)] Guid OperationId, [property: Id(1)] string Model) : Signal;
[GenerateSerializer, Alias("ai.media.EmbeddingsGenerated")]
public sealed record EmbeddingsGenerated([property: Id(0)] Guid OperationId, [property: Id(1)] int Count) : Signal;
[GenerateSerializer, Alias("ai.media.SpeechRecognized")]
public sealed record SpeechRecognized([property: Id(0)] Guid OperationId, [property: Id(1)] string Model) : Signal;
[GenerateSerializer, Alias("ai.media.SpeechSynthesized")]
public sealed record SpeechSynthesized([property: Id(0)] Guid OperationId, [property: Id(1)] string Model) : Signal;
[GenerateSerializer, Alias("ai.media.MediaOperationFailed")]
public sealed record MediaOperationFailed([property: Id(0)] Guid OperationId, [property: Id(1)] MediaOperation Operation,
    [property: Id(2)] string Code) : Signal;