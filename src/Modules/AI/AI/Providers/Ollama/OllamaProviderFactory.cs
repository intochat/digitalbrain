using System.ClientModel;
using Anthropic;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

namespace DigitalBrain.AI;

internal sealed class OllamaProviderFactory : ILlmProviderFactory
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

    public AiProvider Provider => AiProvider.Ollama;

    public bool IsConfigured(AIOptions configuration)
        => !string.IsNullOrEmpty(configuration.Ollama.Endpoint);

    public IChatClient CreateChatClient(LLMModel model, AIOptions configuration)
        => CreateChatClient(configuration.Ollama.Models.GetValueOrDefault(model.Marker.Name)?.Model ?? model.Id, configuration);

    public IChatClient CreateChatClient(string model, AIOptions configuration)
        => new ChatClientBuilder(CreateApiClient(configuration, model, model, "WithLlm", useMarkerOverride: false))
            .ConfigureOptions(static options =>
            {
                options.AdditionalProperties ??= [];
                // A feature/chat turn fits comfortably in 4K and this avoids allocating a
                // workstation-sized KV cache for edge Gemma models in Docker Desktop.
                options.AdditionalProperties.TryAdd("num_ctx", 4096);
                options.AdditionalProperties.TryAdd("think", false);
            })
            .Build();

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        EmbeddingModel model,
        AIOptions configuration)
        => CreateApiClient(configuration, model.Marker.Name, model.Id, "WithEmbedding");

    private static string EndpointConfigurationKey => $"{AIClients.ConfigurationRoot}:Ollama:Endpoint";

    private static OllamaApiClient CreateApiClient(
        AIOptions configuration,
        string marker,
        string defaultTag,
        string hostingMethod,
        bool useMarkerOverride = true)
    {
        var tag = useMarkerOverride ? configuration.Ollama.Models.GetValueOrDefault(marker)?.Model ?? defaultTag : defaultTag;
        var http = new HttpClient
        {
            BaseAddress = RequireEndpoint(configuration, marker, hostingMethod),
            Timeout = RequestTimeout,
        };

        return new OllamaApiClient(http, tag);
    }

    private static Uri RequireEndpoint(AIOptions configuration, string marker, string hostingMethod)
    {
        var endpoint = configuration.Ollama.Endpoint;
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            && (string.Equals(endpointUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(endpointUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return endpointUri;
        }

        throw new InvalidOperationException(
            $"{marker} requires {EndpointConfigurationKey} to be an absolute HTTP(S) URI. "
            + $"Configure it through AIModule.{hostingMethod} in AppHost.");
    }
}