using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace DigitalBrain.SDK.XAI.Grok;

public sealed class GrokConnector : IChatClient
{
    private readonly IChatClient _innerClient;

    public GrokConnector(string apiKey, string modelId)
    {
        if (string.IsNullOrEmpty(apiKey))
            throw new ArgumentException("API key for Grok cannot be null or empty", nameof(apiKey));

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri("https://api.x.ai/v1")
        };
        var openAi = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
        _innerClient = openAi.GetChatClient(modelId).AsIChatClient();
    }

    public ChatClientMetadata Metadata =>
        (_innerClient.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata)
        ?? new ChatClientMetadata("grok", new Uri("https://api.x.ai/v1"));

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return _innerClient.GetResponseAsync(chatMessages, options, cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return _innerClient.GetStreamingResponseAsync(chatMessages, options, cancellationToken);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(ChatClientMetadata))
        {
            return Metadata;
        }
        return _innerClient.GetService(serviceType, serviceKey);
    }

    public void Dispose()
    {
        _innerClient.Dispose();
    }
}
