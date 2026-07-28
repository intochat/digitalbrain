using System.ClientModel;
using System.Diagnostics.CodeAnalysis;
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
    private const string TelemetrySource = "DigitalBrain.AI";

    internal static void Add(IServiceCollection services)
    {
        AddOllamaModel<Llama32>(services, "llama3.2");
        AddOllamaModel<Gemma4>(services, "gemma4:12b");
        AddOllamaModel<Qwen35>(services, "qwen3.5:9b");
        AddOllamaModel<Granite41>(services, "granite4.1:8b");

        services.AddKeyedSingleton<IChatClient>(
            typeof(Gpt56),
            static (provider, _) => OpenAI(provider.GetRequiredService<IConfiguration>()));
    }

    private static void AddOllamaModel<TModel>(IServiceCollection services, string defaultTag)
        where TModel : LLM
        => services.AddKeyedSingleton<IChatClient>(
            typeof(TModel),
            (provider, _) => Ollama(
                provider.GetRequiredService<IConfiguration>(),
                typeof(TModel).Name,
                defaultTag));

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The telemetry middleware owns and disposes the inner Ollama client.")]
    private static IChatClient Ollama(
        IConfiguration configuration,
        string modelName,
        string defaultTag)
    {
        var endpoint = configuration[$"{ConfigurationRoot}:Ollama:Endpoint"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            || endpointUri is null
            || (!string.Equals(endpointUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(endpointUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"{modelName} requires DigitalBrain:AI:Ollama:Endpoint to be an absolute HTTP(S) URI. Configure it through AIModule.WithLlm<{modelName}>() in AppHost.");
        }

        var tag = configuration[$"{ConfigurationRoot}:Ollama:{modelName}:Model"] ?? defaultTag;

        return new ChatClientBuilder(new OllamaApiClient(endpointUri, tag))
            .UseOpenTelemetry(sourceName: $"{TelemetrySource}.{modelName}")
            .Build();
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

        var client = new OpenAIClient(new ApiKeyCredential(apiKey), options)
            .GetChatClient(model)
            .AsIChatClient();

        return new ChatClientBuilder(client)
            .UseOpenTelemetry(sourceName: $"{TelemetrySource}.{nameof(Gpt56)}")
            .Build();
    }
}
