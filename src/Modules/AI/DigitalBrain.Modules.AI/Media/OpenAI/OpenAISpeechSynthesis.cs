using System.ClientModel;
using Microsoft.Extensions.Options;
using global::OpenAI;
using global::OpenAI.Audio;

namespace DigitalBrain.AI.Media.OpenAI;

internal sealed class OpenAISpeechSynthesis(string model, IOptions<AIOptions> options) : ISpeechSynthesisTransport
{
    public bool IsAvailable => !string.IsNullOrWhiteSpace(options.Value.OpenAI.ApiKey);
    public string Model => model;
    public async Task<MediaPayload> Synthesize(SpeechSynthesisRequest request, CancellationToken cancellationToken)
    {
        if (!IsAvailable) { throw new NotSupportedException("OpenAI speech requires DigitalBrain:AI:OpenAI:ApiKey."); }
        var settings = options.Value.OpenAI;
        var clientOptions = new OpenAIClientOptions();
        if (settings.Endpoint is { Length: > 0 } endpoint) { clientOptions.Endpoint = new Uri(endpoint); }
        var client = new OpenAIClient(new ApiKeyCredential(settings.ApiKey!), clientOptions).GetAudioClient(model);
        var response = await client.GenerateSpeechAsync(request.Text, new GeneratedSpeechVoice(request.Voice),
            new SpeechGenerationOptions { ResponseFormat = GeneratedSpeechFormat.Mp3 }, cancellationToken);
        return new MediaPayload(response.Value.ToArray(), "audio/mpeg");
    }
}