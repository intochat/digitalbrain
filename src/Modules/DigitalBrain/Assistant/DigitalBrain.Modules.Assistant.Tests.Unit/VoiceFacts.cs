using DigitalBrain.AI;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;


namespace DigitalBrain.Assistant.Tests.Unit;

public sealed class VoiceFacts
{
    [Fact]
    public async Task AudioIsTranscribedAndReturnedAsDraftText()
    {
        var service = new Transcription();
        var result = await new AssistantTranscription(service).Transcribe(Wav(), TestContext.Current.CancellationToken);
        Assert.Equal(200, result.Status);
        Assert.True(service.Called);
        Assert.Equal("hello", result.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64")]
    [InlineData("aGVsbG8=")]
    public async Task InvalidAudioIsRejectedBeforeCallingProvider(string? audio)
    {
        var service = new Transcription();
        var result = await new AssistantTranscription(service).Transcribe(audio, TestContext.Current.CancellationToken);
        Assert.Equal(400, result.Status);
        Assert.False(service.Called);
    }

    [Fact]
    public async Task MissingProviderReturnsUnavailable()
    {
        var result = await new AssistantTranscription(null).Transcribe(Wav(), TestContext.Current.CancellationToken);
        Assert.Equal(503, result.Status);
    }

    [Fact]
    public async Task ApplicationNeuronOwnsTranscriptionAndRejectsInvalidAudio()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new Transcription();
        await using var brain = await UnitTest.Create().WithModule<AssistantModule>().RequireModules([typeof(DigitalBrain.Apps.AppsModule), typeof(DigitalBrain.AI.AIModule), typeof(DigitalBrain.Compute.ComputeModule), typeof(DigitalBrain.Flutter.FlutterModule)])
            .ConfigureSilo(silo => silo.Services.AddSingleton<IAudioTranscriptionService>(provider))
            .StartAsync(ct);
        var app = brain.Get<IAssistant>(AssistantSurface.Key("voice-workspace"));
        var rejected = await app.Transcribe("invalid", ct);
        Assert.Equal(400, rejected.Status);
        Assert.Equal("Invalid audio encoding.", rejected.Error);
        Assert.False(provider.Called);
        var result = await app.Transcribe(Wav(), ct);
        Assert.Equal(200, result.Status);
        Assert.Equal("hello", result.Text);
        Assert.Null(result.Error);
        Assert.True(provider.Called);
    }

    [Fact]
    public async Task ProviderFailurePreservesPublicErrorAndCancellationPropagates()
    {
        var failed = await new AssistantTranscription(new BrokenTranscription()).Transcribe(Wav(), TestContext.Current.CancellationToken);
        Assert.Equal(502, failed.Status);
        Assert.Equal("Voice transcription failed. Please try again.", failed.Error);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AssistantTranscription(new BrokenTranscription()).Transcribe(Wav(), cancellation.Token));
    }

    private sealed class BrokenTranscription : IAudioTranscriptionService
    {
        public bool IsReady => true;
        public bool InitializationFailed => false;
        public string? ErrorMessage => null;
        public string ModelId => "test";
        public Task<string> TranscribeAsync(string audioFilePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<string> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Private provider details");
        }
    }
    private static string Wav()
    {
        var bytes = new byte[46];
        "RIFF"u8.CopyTo(bytes);
        "WAVE"u8.CopyTo(bytes.AsSpan(8));
        return Convert.ToBase64String(bytes);
    }

    private sealed class Transcription : IAudioTranscriptionService
    {
        public bool Called { get; private set; }
        public bool IsReady => true;
        public bool InitializationFailed => false;
        public string? ErrorMessage => null;
        public string ModelId => "test";
        public Task<string> TranscribeAsync(string audioFilePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<string> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
        {
            Assert.Equal("voice.wav", fileName);
            Assert.Equal(46, audioStream.Length);
            Called = true;
            return Task.FromResult("hello");
        }
    }
}
