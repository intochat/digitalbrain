using System.ClientModel;
using DigitalBrain.AI.Ollama;
using DigitalBrain.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;
using OpenAI;

namespace DigitalBrain.AI;

internal static class AIClients
{
    private const string ConfigurationRoot = "DigitalBrain:AI";

    internal static void Add(IServiceCollection services)
    {
        services.AddKeyedSingleton<IChatClient>(
            typeof(Llama32),
            static (provider, _) => Ollama(provider.GetRequiredService<IConfiguration>()));
        services.AddKeyedSingleton<IChatClient>(
            typeof(Gpt56),
            static (provider, _) => OpenAI(provider.GetRequiredService<IConfiguration>()));
    }

    private static OllamaApiClient Ollama(IConfiguration configuration)
    {
        var endpoint = configuration[$"{ConfigurationRoot}:Ollama:Endpoint"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            || endpointUri is null
            || (!string.Equals(endpointUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(endpointUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Llama32 requires DigitalBrain:AI:Ollama:Endpoint to be an absolute HTTP(S) URI. Configure it through AIModule.WithLlm<Llama32>() in AppHost.");
        }

        var model = configuration[$"{ConfigurationRoot}:Ollama:Llama32:Model"] ?? "llama3.2";

        return new OllamaApiClient(endpointUri, model);
    }

    private static IChatClient OpenAI(IConfiguration configuration)
    {
        var apiKey = configuration[$"{ConfigurationRoot}:OpenAI:ApiKey"]
            ?? throw new InvalidOperationException(
                "Gpt56 requires DigitalBrain:AI:OpenAI:ApiKey. Configure it through AIModule.WithLlm<Gpt56>() in AppHost.");
        var model = configuration[$"{ConfigurationRoot}:OpenAI:Gpt56:Model"] ?? "gpt-5.6";
        var options = new OpenAIClientOptions();

        if (configuration[$"{ConfigurationRoot}:OpenAI:Endpoint"] is { } endpoint)
        {
            options.Endpoint = new Uri(endpoint, UriKind.Absolute);
        }

        return new OpenAIClient(new ApiKeyCredential(apiKey), options)
            .GetChatClient(model)
            .AsIChatClient();
    }
}
