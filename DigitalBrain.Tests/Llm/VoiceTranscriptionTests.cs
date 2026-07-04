using DigitalBrain.Core.Models;
using DigitalBrain.Kernel.Voice;
using Microsoft.Extensions.Configuration;

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
                ["DigitalBrain:ModelRegistry:Registrations:0:Id"] = "qwen2.5-coder:1.5b",
                ["DigitalBrain:ModelRegistry:Registrations:1:Kind"] = DigitalBrainCapabilityKind.VoiceToText.ToString(),
                ["DigitalBrain:ModelRegistry:Registrations:1:Provider"] = DigitalBrainProviderIds.OpenAI,
                ["DigitalBrain:ModelRegistry:Registrations:1:Id"] = "whisper-test"
            })
            .Build();

        var options = DigitalBrainVoiceRuntimeOptions.FromConfiguration(config);

        Assert.Equal(DigitalBrainProviderIds.OpenAI, options.Provider);
        Assert.Equal("whisper-test", options.Model);
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
}
