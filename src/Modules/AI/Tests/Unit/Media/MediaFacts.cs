using DigitalBrain.AI;
using DigitalBrain.AI.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using IImageGenerator = DigitalBrain.AI.Media.IImageGenerator;
using ImageGenerationRequest = DigitalBrain.AI.Media.ImageGenerationRequest;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class MediaFacts
{
    [Fact]
    public async Task EmbeddingsRecognitionAndSynthesisUseConfiguredServices()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AIModule>()
            .ConfigureSilo(s =>
            {
                s.Services.AddMediaNeurons();
                s.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new Embeddings());
                s.Services.AddSingleton<IAudioTranscriptionService>(new Transcription());
                s.Services.AddSingleton<ISpeechSynthesisTransport>(new Speech());
            }).StartAsync(ct);
        var embeddings = await brain.Get<IEmbeddingModel>("embedding").Embed(new EmbeddingRequest(["one", "two"]), ct);
        Assert.Equal(2, embeddings.Vectors.Length);
        Assert.Equal(new float[] { 3, 1 }, embeddings.Vectors[0]);
        var recognition = await brain.Get<ISpeechRecognizer>("recognition")
            .Recognize(new SpeechRecognitionRequest(new MediaPayload([4, 5], "audio/wav"), "clip.wav"), ct);
        Assert.Equal("clip.wav:2", recognition.Text);
        var speech = await brain.Get<ISpeechSynthesizer>("speech").Synthesize(new SpeechSynthesisRequest("hello", "voice"), ct);
        Assert.Equal("audio/mpeg", speech.Audio.MediaType);
        Assert.Equal(new byte[] { 5 }, speech.Audio.Content);
    }

    [Fact]
    public async Task OversizedInputIsRejectedBeforeProviderInvocation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AIModule>()
            .ConfigureSilo(s => { s.Services.AddMediaNeurons(); s.Services.AddSingleton<IImageGeneration>(new Images()); })
            .StartAsync(ct);
        var image = brain.Get<IImageGenerator>("invalid");
        await using var failed = await brain.Observe<MediaOperationFailed>(image, ct);
        await Assert.ThrowsAsync<ArgumentException>(() => image.Generate(new ImageGenerationRequest(new string('x', 32769)), ct));
        Assert.Equal("failed", (await failed.NextAsync(ct: ct)).Code);
    }

    [Fact]
    public async Task CancellationReachesTheRunningProvider()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new BlockingImages();
        await using var brain = await UnitTest.Create().WithModule<AIModule>()
            .ConfigureSilo(s => { s.Services.AddMediaNeurons(); s.Services.AddSingleton<IImageGeneration>(service); })
            .StartAsync(ct);
        var neuron = brain.Get<IImageGenerator>("cancel");
        await using var failed = await brain.Observe<MediaOperationFailed>(neuron, ct);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pending = neuron.Generate(new ImageGenerationRequest("tree"), cancellation.Token);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal("cancelled", (await failed.NextAsync(ct: ct)).Code);
    }

    [Fact]
    public async Task ImageGenerationReturnsSerializableBytesAndPublishesCompletion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AIModule>()
            .ConfigureSilo(s => { s.Services.AddMediaNeurons(); s.Services.AddSingleton<IImageGeneration>(new Images()); })
            .StartAsync(ct);
        var neuron = brain.Get<IImageGenerator>("image");
        await using var completed = await brain.Observe<ImageGenerated>(neuron, ct);
        var result = await neuron.Generate(new ImageGenerationRequest("tree"), ct);
        Assert.Equal(new byte[] { 1, 2 }, result.Image.Content);
        Assert.Equal("test-image", result.Model);
        Assert.Equal(result.OperationId, (await completed.NextAsync(ct: ct)).OperationId);
    }

    [Fact]
    public async Task SynthesisReportsUnavailableAndPublishesTypedFailure()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AIModule>()
            .ConfigureSilo(s => s.Services.AddMediaNeurons()).StartAsync(ct);
        var neuron = brain.Get<ISpeechSynthesizer>("speech");
        Assert.False((await neuron.Describe()).Available);
        await using var failed = await brain.Observe<MediaOperationFailed>(neuron, ct);
        await Assert.ThrowsAsync<NotSupportedException>(() => neuron.Synthesize(new SpeechSynthesisRequest("hello", "voice"), ct));
        Assert.Equal(MediaOperation.SpeechSynthesis, (await failed.NextAsync(ct: ct)).Operation);
    }

    private sealed class Images : IImageGeneration
    {
        public Task<GeneratedUiImage> GenerateAsync(string prompt, CancellationToken cancellationToken)
            => Task.FromResult(new GeneratedUiImage([1, 2], "image/png", "test-image"));
    }

    private sealed class BlockingImages : IImageGeneration
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<GeneratedUiImage> GenerateAsync(string prompt, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }
    private sealed class Embeddings : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(values.Select(x => new Embedding<float>(new float[] { x.Length, 1 }))));
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
    private sealed class Transcription : IAudioTranscriptionService
    {
        public bool IsReady => true;
        public bool InitializationFailed => false;
        public string? ErrorMessage => null;
        public string ModelId => "test-recognition";
        public Task<string> TranscribeAsync(string audioFilePath, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The neuron must supply bytes, not paths.");
        public Task<string> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
            => Task.FromResult($"{fileName}:{audioStream.Length}");
    }
    private sealed class Speech : ISpeechSynthesisTransport
    {
        public bool IsAvailable => true;
        public string Model => "test-speech";
        public Task<MediaPayload> Synthesize(SpeechSynthesisRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new MediaPayload([(byte)request.Text.Length], "audio/mpeg"));
    }
}