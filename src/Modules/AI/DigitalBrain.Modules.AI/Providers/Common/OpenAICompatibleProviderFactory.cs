using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace DigitalBrain.AI;

internal abstract class OpenAICompatibleProviderFactory : ApiKeyProviderFactory
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

    public override IChatClient CreateChatClient(string model, AIOptions configuration, IAiCredentials credentials, string? endpoint = null)
    {
        var builder = new ChatClientBuilder(
            CreateClient(credentials, endpoint).GetChatClient(model).AsIChatClient());

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
        AIOptions configuration,
        IAiCredentials credentials)
        => CreateClient(credentials, null).GetEmbeddingClient(model.Id).AsIEmbeddingGenerator();

    private OpenAIClient CreateClient(IAiCredentials credentials, string? pinnedEndpoint)
    {
        var options = new OpenAIClientOptions { NetworkTimeout = RequestTimeout, Endpoint = EndpointOf(credentials, pinnedEndpoint) };
        return new OpenAIClient(new ApiKeyCredential(ReleaseApiKey(credentials)), options);
    }
}
