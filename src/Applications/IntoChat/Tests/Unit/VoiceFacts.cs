using DigitalBrain.AI;
using IntoChat.Agent;
using Microsoft.AspNetCore.Http;

namespace IntoChat.Tests;

public sealed class VoiceFacts
{
    [Fact]
    public async Task AudioIsTranscribedAndReturnedAsDraftText()
    {
        var service = new Transcription();
        var result = await VoiceEndpoints.Transcribe(new(Wav()), service, CancellationToken.None);
        Assert.Equal(200, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.True(service.Called);
        Assert.Contains("hello", System.Text.Json.JsonSerializer.Serialize(((IValueHttpResult)result).Value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64")]
    [InlineData("aGVsbG8=")]
    public async Task InvalidAudioIsRejectedBeforeCallingProvider(string? audio)
    {
        var service = new Transcription();
        var result = await VoiceEndpoints.Transcribe(new(audio), service, CancellationToken.None);
        Assert.Equal(400, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.False(service.Called);
    }

    [Fact]
    public async Task MissingProviderReturnsUnavailable()
    {
        var result = await VoiceEndpoints.Transcribe(new(Wav()), null, CancellationToken.None);
        Assert.Equal(503, ((IStatusCodeHttpResult)result).StatusCode);
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
