using DigitalBrain.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Aspire.Tests;

// One key names a marker and the model's provider picks the implementation, so
// the interesting cases are all about what happens when that key is absent,
// wrong, or names a model whose provider has no implementation.
public sealed class VoiceToTextSelectionTests
{
    private const string TranscriptionKey = "DigitalBrain:AI:Default:Transcription";
    private const string OpenAIKeyKey = "DigitalBrain:AI:OpenAI:ApiKey";
    private const string HostedMarker = nameof(DigitalBrain.AI.OpenAI.IGpt4oMiniTranscribe);

    private static IAudioTranscriptionService Resolve(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(static setting =>
                new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        VoiceToTextHosting.Add(services, configuration);

        return services.BuildServiceProvider().GetRequiredService<IAudioTranscriptionService>();
    }

    [Fact]
    public void NoConfiguredModelLeavesVoiceUnavailable()
    {
        var service = Resolve();

        Assert.False(service.IsReady);
        Assert.NotNull(service.ErrorMessage);
        Assert.Contains("not configured", service.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnUnknownMarkerReportsItselfRatherThanThrowing()
    {
        // The kernel must still boot: voice degrades to 503 carrying this reason.
        var service = Resolve((TranscriptionKey, "INotAModel"), (OpenAIKeyKey, "sk-test"));

        Assert.False(service.IsReady);
        Assert.NotNull(service.ErrorMessage);
        Assert.Contains("INotAModel", service.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(HostedMarker, service.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void AHostedModelWithAKeyIsReady()
    {
        var service = Resolve((TranscriptionKey, HostedMarker), (OpenAIKeyKey, "sk-test"));

        Assert.True(service.IsReady);
        Assert.Equal("gpt-4o-mini-transcribe", service.ModelId);
    }

    [Fact]
    public void AHostedModelWithoutAKeyNamesTheMissingSetting()
    {
        var service = Resolve((TranscriptionKey, HostedMarker));

        Assert.False(service.IsReady);
        Assert.NotNull(service.ErrorMessage);
        Assert.Contains(OpenAIKeyKey, service.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ALocalModelSelectsTheFoundryImplementation()
    {
        // Not IsReady: Foundry only becomes ready once its hosted service has
        // downloaded and loaded the model. Selection is what is under test.
        var service = Resolve(
            (TranscriptionKey, nameof(DigitalBrain.AI.FoundryLocal.IWhisperLargeV3Turbo)));

        Assert.IsType<FoundryLocalTranscriptionService>(service);
    }

    [Theory]
    [InlineData("voice.wav", "voice.wav")]
    [InlineData("note.webm", "note.webm")]
    [InlineData("recording.OGG", "recording.OGG")]
    [InlineData("blob", "voice.wav")]
    [InlineData("clip.bin", "voice.wav")]
    [InlineData("", "voice.wav")]
    // Opus rides in an Ogg container the provider accepts, but .opus is not in its
    // extension list. Relabelling it .wav would announce Ogg bytes as WAV.
    [InlineData("note.opus", "note.ogg")]
    [InlineData("NOTE.OPUS", "NOTE.ogg")]
    public void UnrecognisedContainersFallBackToWav(string given, string expected)
    {
        // An extension the provider does not know is a 400 that would surface as an
        // opaque transcription failure.
        Assert.Equal(expected, OpenAITranscriptionService.NormalizeFileName(given));
    }
}
