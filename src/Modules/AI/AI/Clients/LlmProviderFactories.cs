using System.ClientModel;
using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OllamaSharp;
using OpenAI;

namespace DigitalBrain.AI;

internal interface ILlmProviderFactory
{
    AiProvider Provider { get; }

    bool IsConfigured(IConfiguration configuration);

    IChatClient CreateChatClient(LLMModel model, IConfiguration configuration);

    IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        EmbeddingModel model,
        IConfiguration configuration);
}

internal abstract class ApiKeyProviderFactory : ILlmProviderFactory
{
    public abstract AiProvider Provider { get; }

    public bool IsConfigured(IConfiguration configuration)
        => !string.IsNullOrEmpty(configuration[ApiKeyConfigurationKey]);

    public abstract IChatClient CreateChatClient(LLMModel model, IConfiguration configuration);

    public abstract IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        EmbeddingModel model,
        IConfiguration configuration);

    protected string ApiKeyConfigurationKey => $"{AIClients.ConfigurationRoot}:{Provider}:ApiKey";

    protected string RequireApiKey(IConfiguration configuration, Type marker, string hostingMethod)
        => configuration[ApiKeyConfigurationKey] is { Length: > 0 } apiKey
            ? apiKey
            : throw new InvalidOperationException(
                $"{marker.Name} requires {ApiKeyConfigurationKey}. Configure the model through "
                + $"AIModule.{hostingMethod}<{marker.Name}>() in AppHost and supply the "
                + $"{Provider.ToString().ToLowerInvariant()}-api-key secret parameter.");
}

internal abstract class OpenAICompatibleProviderFactory : ApiKeyProviderFactory
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

    protected abstract Uri? DefaultEndpoint { get; }

    public override IChatClient CreateChatClient(LLMModel model, IConfiguration configuration)
        => new ChatClientBuilder(
                CreateClient(configuration, model.Marker, "WithLlm").GetChatClient(model.Id).AsIChatClient())
            .UseStreamingUsage()
            .Build();

    public override IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        EmbeddingModel model,
        IConfiguration configuration)
        => CreateClient(configuration, model.Marker, "WithEmbedding").GetEmbeddingClient(model.Id).AsIEmbeddingGenerator();

    private OpenAIClient CreateClient(IConfiguration configuration, Type marker, string hostingMethod)
    {
        var options = new OpenAIClientOptions { NetworkTimeout = RequestTimeout };
        var endpoint = configuration[$"{AIClients.ConfigurationRoot}:{Provider}:Endpoint"] is { Length: > 0 } configured
            ? new Uri(configured)
            : DefaultEndpoint;
        if (endpoint is not null)
        {
            options.Endpoint = endpoint;
        }

        return new OpenAIClient(new ApiKeyCredential(RequireApiKey(configuration, marker, hostingMethod)), options);
    }
}

internal sealed class OpenAIProviderFactory : OpenAICompatibleProviderFactory
{
    public override AiProvider Provider => AiProvider.OpenAI;

    protected override Uri? DefaultEndpoint => null;
}

internal sealed class GoogleProviderFactory : OpenAICompatibleProviderFactory
{
    public override AiProvider Provider => AiProvider.Google;

    protected override Uri? DefaultEndpoint { get; } =
        new("https://generativelanguage.googleapis.com/v1beta/openai/");
}

internal sealed class XAIProviderFactory : OpenAICompatibleProviderFactory
{
    public override AiProvider Provider => AiProvider.XAI;

    protected override Uri? DefaultEndpoint { get; } = new("https://api.x.ai/v1");
}

internal sealed class AnthropicProviderFactory : ApiKeyProviderFactory
{
    public override AiProvider Provider => AiProvider.Anthropic;

    public override IChatClient CreateChatClient(LLMModel model, IConfiguration configuration)
        => new AnthropicClient
        {
            ApiKey = RequireApiKey(configuration, model.Marker, "WithLlm"),
            Timeout = TimeSpan.FromMinutes(5),
        }.AsIChatClient(model.Id);

    public override IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        EmbeddingModel model,
        IConfiguration configuration)
        => throw new NotSupportedException("Anthropic does not provide embedding models.");
}

internal sealed class OllamaProviderFactory : ILlmProviderFactory
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

    public AiProvider Provider => AiProvider.Ollama;

    public bool IsConfigured(IConfiguration configuration)
        => !string.IsNullOrEmpty(configuration[EndpointConfigurationKey]);

    public IChatClient CreateChatClient(LLMModel model, IConfiguration configuration)
        => new ChatClientBuilder(CreateApiClient(configuration, model.Marker, model.Id, "WithLlm"))
            .ConfigureOptions(static options =>
            {
                options.AdditionalProperties ??= [];
                options.AdditionalProperties["num_ctx"] = 16384;
                options.AdditionalProperties["think"] = false;
            })
            .Build();

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        EmbeddingModel model,
        IConfiguration configuration)
        => CreateApiClient(configuration, model.Marker, model.Id, "WithEmbedding");

    private static string EndpointConfigurationKey => $"{AIClients.ConfigurationRoot}:Ollama:Endpoint";

    private static OllamaApiClient CreateApiClient(
        IConfiguration configuration,
        Type marker,
        string defaultTag,
        string hostingMethod)
    {
        var tag = configuration[$"{AIClients.ConfigurationRoot}:Ollama:{marker.Name}:Model"] ?? defaultTag;
        var http = new HttpClient
        {
            BaseAddress = RequireEndpoint(configuration, marker, hostingMethod),
            Timeout = RequestTimeout,
        };

        return new OllamaApiClient(http, tag);
    }

    private static Uri RequireEndpoint(IConfiguration configuration, Type marker, string hostingMethod)
    {
        var endpoint = configuration[EndpointConfigurationKey];
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            && (string.Equals(endpointUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(endpointUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return endpointUri;
        }

        throw new InvalidOperationException(
            $"{marker.Name} requires {EndpointConfigurationKey} to be an absolute HTTP(S) URI. "
            + $"Configure it through AIModule.{hostingMethod}<{marker.Name}>() in AppHost.");
    }
}
