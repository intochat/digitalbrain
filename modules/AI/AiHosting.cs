using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Brain.Kernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;
using Orleans.Hosting;

namespace Brain.Modules.Ai;

public static class AiHosting
{
    public static ISiloBuilder AddDigitalBrainAI(this ISiloBuilder silo, IConfiguration config)
    {
        var catalog = ModelCatalog.FromConfiguration(config);
        silo.Services.AddSingleton(catalog);

        var ollamaEndpoint = config["Brain:Ai:OllamaEndpoint"] ?? "http://localhost:11434";
        var balancedModel = catalog.Resolve(ModelTier.Balanced).Model;
        silo.Services.AddKeyedSingleton<IChatClient>("ollama", (_, _) =>
            new OllamaApiClient(new Uri(ollamaEndpoint), balancedModel));

        var azureOpenAiEndpoint = config["Brain:Ai:AzureOpenAIEndpoint"];
        if (!string.IsNullOrWhiteSpace(azureOpenAiEndpoint))
        {
            var azureOpenAiKey = config["Brain:Ai:AzureOpenAIKey"];
            silo.Services.AddKeyedSingleton<IChatClient>("azureopenai", (_, _) =>
                (string.IsNullOrWhiteSpace(azureOpenAiKey)
                    ? new AzureOpenAIClient(new Uri(azureOpenAiEndpoint), new DefaultAzureCredential())
                    : new AzureOpenAIClient(new Uri(azureOpenAiEndpoint), new AzureKeyCredential(azureOpenAiKey)))
                    .GetChatClient(balancedModel)
                    .AsIChatClient());
        }

        return silo.AddBrainKind("llm", sp => new LlmKind(catalog, sp));
    }
}
