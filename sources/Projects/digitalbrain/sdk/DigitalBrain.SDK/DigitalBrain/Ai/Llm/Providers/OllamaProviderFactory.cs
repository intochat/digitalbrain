using System.ClientModel;
using DigitalBrain.SDK.DigitalBrain.Ai.Llm;
using DigitalBrain.SDK.DigitalBrain.Ai.Models;
using Microsoft.Extensions.AI;
using OpenAI;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Llm.Providers;

public sealed class OllamaProviderFactory : ILlmProviderFactory
{
    public string ProviderName => "ollama";

    public bool IsConfigured(IConfiguration config)
        => !string.IsNullOrEmpty(config["services:ollama:http:0"]);

    public IChatClient CreateClient(LlmModel model, IConfiguration config)
    {
        var endpoint = config["services:ollama:http:0"];
        if (string.IsNullOrEmpty(endpoint))
        {
            endpoint = "http://localhost:11434";
        }

        var baseUri = new Uri(endpoint);
        var openAiEndpoint = new Uri(baseUri, "/v1");

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = openAiEndpoint
        };

        var openAiClient = new OpenAIClient(new ApiKeyCredential("ollama"), clientOptions);
        var modelId = config["DigitalBrain:Ai:LocalModel"] ?? model.Id;
        if (string.IsNullOrEmpty(modelId))
        {
            modelId = "llama3";
        }
        return openAiClient.GetChatClient(modelId).AsIChatClient();
    }
}
