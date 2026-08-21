using DigitalBrain.AI.Ollama;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OllamaSharp;

namespace DigitalBrain.AI;

internal static class AIClients
{
    internal const string ConfigurationRoot = "DigitalBrain:AI";
    private const string EnableSensitiveDataKey =
        $"{ConfigurationRoot}:Telemetry:EnableSensitiveData";
    private const string TelemetrySource = "DigitalBrain.AI";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

    internal static void Add(IServiceCollection services)
    {
        AddOllamaModel<IGemma4>(services, "gemma4:12b");
        AddOllamaEmbedding<IEmbeddingGemma>(services, "embeddinggemma");

        services.AddHostedService<LlmWarmupHostedService>();
    }

    private static void AddOllamaModel<TModel>(IServiceCollection services, string defaultTag)
        where TModel : class
        => services.AddKeyedSingleton<IChatClient>(
            typeof(TModel),
            (provider, _) => Ollama(
                provider.GetRequiredService<IConfiguration>(),
                typeof(TModel).Name,
                defaultTag));

    private static void AddOllamaEmbedding<TModel>(IServiceCollection services, string defaultTag)
        where TModel : class
        => services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            typeof(TModel),
            (provider, _) => OllamaEmbedding(
                provider.GetRequiredService<IConfiguration>(),
                typeof(TModel).Name,
                defaultTag));

    private static IEmbeddingGenerator<string, Embedding<float>> OllamaEmbedding(
        IConfiguration configuration,
        string modelName,
        string defaultTag)
    {
        var endpoint = RequireOllamaEndpoint(configuration, modelName);
        var tag = configuration[$"{ConfigurationRoot}:Ollama:{modelName}:Model"] ?? defaultTag;
        var http = new HttpClient
        {
            BaseAddress = endpoint,
            Timeout = RequestTimeout,
        };

        return new OllamaApiClient(http, tag);
    }

    private static IChatClient Ollama(
        IConfiguration configuration,
        string modelName,
        string defaultTag)
    {
        var endpointUri = RequireOllamaEndpoint(configuration, modelName);
        var tag = configuration[$"{ConfigurationRoot}:Ollama:{modelName}:Model"] ?? defaultTag;
        var enableSensitiveData = configuration.GetValue<bool>(EnableSensitiveDataKey);

        var http = new HttpClient
        {
            BaseAddress = endpointUri,
            Timeout = RequestTimeout,
        };

        return new ChatClientBuilder(new OllamaApiClient(http, tag))
            .UseOpenTelemetry(
                sourceName: $"{TelemetrySource}.{modelName}",
                configure: telemetry => telemetry.EnableSensitiveData = enableSensitiveData)
            .Build();
    }

    private static Uri RequireOllamaEndpoint(IConfiguration configuration, string modelName)
    {
        var endpoint = configuration[$"{ConfigurationRoot}:Ollama:Endpoint"];
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            && endpointUri is not null
            && (string.Equals(endpointUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(endpointUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return endpointUri;
        }

        throw new InvalidOperationException(
            $"{modelName} requires DigitalBrain:AI:Ollama:Endpoint to be an absolute HTTP(S) URI. Configure it through AIModule.WithLlm<{modelName}>() in AppHost.");
    }

}
