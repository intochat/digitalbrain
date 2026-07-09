using System.Net;
using System.Net.Http.Headers;
using DigitalBrain.Core.Models;
using DigitalBrain.Kernel.Voice;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests.Llm;

public sealed class VoiceTranscriptionTests
{
    [Fact]
    public void RuntimeOptionsUseVoiceToTextModelRegistryRegistration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:ModelRegistry:Registrations:0:Kind"] = DigitalBrainCapabilityKind.LargeLanguageModel.ToString(),
                ["DigitalBrain:ModelRegistry:Registrations:0:Provider"] = DigitalBrainProviderIds.Ollama,
                ["DigitalBrain:ModelRegistry:Registrations:0:Id"] = "llama3.1:8b",
                ["DigitalBrain:ModelRegistry:Registrations:1:Kind"] = DigitalBrainCapabilityKind.VoiceToText.ToString(),
                ["DigitalBrain:ModelRegistry:Registrations:1:Provider"] = DigitalBrainProviderIds.OpenAI,
                ["DigitalBrain:ModelRegistry:Registrations:1:Id"] = "whisper-test",
                ["DigitalBrain:Voice:Endpoint"] = "http://localhost:8080/v1",
                ["DigitalBrain:Voice:ApiKey"] = "local-key"
            })
            .Build();

        var options = DigitalBrainVoiceRuntimeOptions.FromConfiguration(config);

        Assert.Equal(DigitalBrainProviderIds.OpenAI, options.Provider);
        Assert.Equal("whisper-test", options.Model);
        Assert.Equal("http://localhost:8080/v1", options.Endpoint);
        Assert.Equal("http://localhost:8080/v1/audio/transcriptions", options.TranscriptionEndpoint);
        Assert.Equal("local-key", options.ApiKey);
        Assert.True(options.IsConfigured);
    }

    [Fact]
    public async Task NoOpTranscriberReturnsEmptyTranscriptWithCorrelationId()
    {
        var transcriber = new NoOpVoiceTranscriber(DigitalBrainVoiceRuntimeOptions.Unconfigured);

        var result = await transcriber.TranscribeAsync(new VoiceTranscriptionRequest(
            new byte[] { 1, 2, 3 },
            "audio/wav",
            "en",
            "voice-123"));

        Assert.Equal(string.Empty, result.Transcript);
        Assert.Equal("voice-123", result.CorrelationId);
    }

    [Fact]
    public void AddDigitalBrainVoiceTranscriptionRegistersOpenAICompatibleAdapterWhenEndpointIsConfigured()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:Voice:Provider"] = DigitalBrainProviderIds.OpenAICompatible,
                ["DigitalBrain:Voice:Model"] = "whisper-1",
                ["DigitalBrain:Voice:Endpoint"] = "http://localhost:8080/v1"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddDigitalBrainVoiceTranscription(config);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<OpenAICompatibleVoiceTranscriber>(provider.GetRequiredService<IVoiceTranscriber>());
    }

    [Fact]
    public async Task OpenAICompatibleTranscriberPostsMultipartAudioAndParsesTranscript()
    {
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(async (request, ct) =>
        {
            captured = request;
            var body = await request.Content!.ReadAsStringAsync(ct);
            Assert.Contains("name=file; filename=audio.wav", body);
            Assert.Contains("name=model", body);
            Assert.Contains("whisper-test", body);
            Assert.Contains("name=language", body);
            Assert.Contains("en", body);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"text\":\"turn on the lights\",\"language\":\"en\"}")
            };
        });
        var options = new DigitalBrainVoiceRuntimeOptions(
            DigitalBrainProviderIds.OpenAICompatible,
            "whisper-test",
            "http://localhost:8080/v1",
            "local-key");
        var transcriber = new OpenAICompatibleVoiceTranscriber(options, new HttpClient(handler));

        var result = await transcriber.TranscribeAsync(new VoiceTranscriptionRequest(
            new byte[] { 1, 2, 3 },
            "audio/wav",
            "en",
            "voice-456"));

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal(new Uri("http://localhost:8080/v1/audio/transcriptions"), captured.RequestUri);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "local-key"), captured.Headers.Authorization);
        Assert.Equal("turn on the lights", result.Transcript);
        Assert.Equal("en", result.DetectedLanguage);
        Assert.Equal("voice-456", result.CorrelationId);
    }

    [Fact]
    public async Task OpenAICompatibleTranscriberFallsBackToEmptyTranscriptOnHttpFailure()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var options = new DigitalBrainVoiceRuntimeOptions(
            DigitalBrainProviderIds.OpenAICompatible,
            "whisper-test",
            "http://localhost:8080/v1");
        var transcriber = new OpenAICompatibleVoiceTranscriber(options, new HttpClient(handler));

        var result = await transcriber.TranscribeAsync(new VoiceTranscriptionRequest(
            new byte[] { 1 },
            "audio/wav",
            null,
            "voice-789"));

        Assert.Equal(string.Empty, result.Transcript);
        Assert.Equal("voice-789", result.CorrelationId);
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            sendAsync(request, cancellationToken);
    }
}
