using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.Media;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests;

public sealed class SpeechTransportFacts
{
    [Fact]
    public async Task OpenAISpeechMapsModelVoiceTextAndReturnsMp3WithCompletionSignal()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;
        using var endpoint = new LoopbackServer();
        await using var brain = await UnitTest.Create().WithRegistrations(AiRegistrationSeeds.OpenAI(endpoint: endpoint.Url)).WithModule<AIModule>()
            .ConfigureSilo(silo =>
            {
                silo.Services.AddOpenAISpeechSynthesis("fixture-tts");
            }).StartAsync(ct);
        var speech = brain.Get<ISpeechSynthesizer>("real-speech-transport");
        Assert.True((await speech.Describe()).Available);
        await using var completed = await brain.Observe<SpeechSynthesized>(speech, ct);
        byte[] mp3 = [0x49, 0x44, 0x33, 0x04, 0, 0];
        var requestTask = endpoint.ReplyOnce("audio/mpeg", mp3, ct);
        var response = await speech.Synthesize(new SpeechSynthesisRequest("Read this aloud.", "coral"), ct);
        var request = await requestTask;
        using var json = JsonDocument.Parse(request.Body);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/audio/speech", request.Path);
        Assert.Equal("fixture-tts", json.RootElement.GetProperty("model").GetString());
        Assert.Equal("coral", json.RootElement.GetProperty("voice").GetString());
        Assert.Equal("Read this aloud.", json.RootElement.GetProperty("input").GetString());
        Assert.Equal("mp3", json.RootElement.GetProperty("response_format").GetString());
        Assert.Equal(mp3, response.Audio.Content);
        Assert.Equal("audio/mpeg", response.Audio.MediaType);
        Assert.Equal("fixture-tts", response.Model);
        var signal = await completed.NextAsync(ct: ct);
        Assert.Equal(response.OperationId, signal.OperationId);
        Assert.Equal(response.Model, signal.Model);
    }
}
