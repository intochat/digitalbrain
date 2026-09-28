using System.ClientModel;
using Anthropic;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

namespace DigitalBrain.AI;

internal abstract class OpenAICompatibleProviderFactory : ApiKeyProviderFactory
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

    protected abstract Uri? DefaultEndpoint { get; }

    public override IChatClient CreateChatClient(string model, AIOptions configuration)
    {
        var builder = new ChatClientBuilder(
            CreateClient(configuration, model, "WithLlm").GetChatClient(model).AsIChatClient());

        if (Provider is AiProvider.OpenAI && model.StartsWith("gpt-5.6", StringComparison.OrdinalIgnoreCase))
        {
            builder.ConfigureOptions(static options =>
            {
                // GPT-5.6 Chat Completions rejects function tools when reasoning
                // effort is omitted or enabled. Keep an explicit caller choice,
                // but use the API-supported "none" value otherwise.
                if (options.Tools is { Count: > 0 } && options.Reasoning is null)
                {
                    options.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None };
                }
            });
        }

        return builder
            .UseStreamingUsage()
            .Build();
    }

    public override IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        EmbeddingModel model,
        AIOptions configuration)
        => CreateClient(configuration, model.Marker.Name, "WithEmbedding").GetEmbeddingClient(model.Id).AsIEmbeddingGenerator();

    private OpenAIClient CreateClient(AIOptions configuration, string model, string hostingMethod)
    {
        var options = new OpenAIClientOptions { NetworkTimeout = RequestTimeout };
        var endpoint = configuration.Provider(Provider).Endpoint is { Length: > 0 } configured
            ? new Uri(configured)
            : DefaultEndpoint;
        if (endpoint is not null)
        {
            options.Endpoint = endpoint;
        }

        return new OpenAIClient(new ApiKeyCredential(RequireApiKey(configuration, model, hostingMethod)), options);
    }
}