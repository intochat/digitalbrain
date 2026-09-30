using System.ClientModel;
using Microsoft.Extensions.Options;
using global::OpenAI;
using global::OpenAI.Audio;

namespace DigitalBrain.AI.Media.OpenAI;

internal sealed class OpenAISpeechSynthesis(string model, IAiCredentials credentials) : ISpeechSynthesisTransport
{
    public bool IsAvailable => credentials.IsReady(AiIntegrations.IdOf(AiProvider.OpenAI));
    public string Model => model;
    public async Task<MediaPayload> Synthesize(SpeechSynthesisRequest request, CancellationToken cancellationToken)
    {
        var integration = AiIntegrations.IdOf(AiProvider.OpenAI);
        var apiKey = await credentials.ReleaseSecretAsync(integration, AiIntegrations.ApiKeyField, cancellationToken);
        var clientOptions = new OpenAIClientOptions();
        if (credentials.Setting(integration, AiIntegrations.EndpointField) is { Length: > 0 } endpoint) { clientOptions.Endpoint = new Uri(endpoint); }
        var client = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions).GetAudioClient(model);
        var response = await client.GenerateSpeechAsync(request.Text, new GeneratedSpeechVoice(request.Voice),
            new SpeechGenerationOptions { ResponseFormat = GeneratedSpeechFormat.Mp3 }, cancellationToken);
        return new MediaPayload(response.Value.ToArray(), "audio/mpeg");
    }
}