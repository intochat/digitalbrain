using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace DigitalBrain.Kernel;

internal static class OpenAIProviderClientFactory
{
    public static OpenAIClient CreateProvider(
        OpenAIProviderOptions options,
        HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = options.Endpoint!
        };
        if (httpClient is not null)
            clientOptions.Transport = new HttpClientPipelineTransport(httpClient);

        return new OpenAIClient(
            new ApiKeyCredential(options.ApiKey!),
            clientOptions);
    }

    public static IChatClient CreateChat(
        OpenAIProviderOptions options,
        string modelId,
        ILoggerFactory loggerFactory,
        HttpClient? httpClient = null) =>
        CreateChat(CreateProvider(options, httpClient), modelId, loggerFactory);

    public static IChatClient CreateChat(
        OpenAIClient provider,
        string modelId,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        return new ChatClientBuilder(provider.GetChatClient(modelId).AsIChatClient())
            .UseOpenTelemetry(
                loggerFactory,
                DigitalBrainAIHosting.TelemetrySourceName,
                telemetry => telemetry.EnableSensitiveData = false)
            .Build();
    }

    public static IEmbeddingGenerator<string, Embedding<float>> CreateEmbedding(
        OpenAIProviderOptions options,
        string modelId,
        ILoggerFactory loggerFactory,
        HttpClient? httpClient = null) =>
        CreateEmbedding(CreateProvider(options, httpClient), modelId, loggerFactory);

    public static IEmbeddingGenerator<string, Embedding<float>> CreateEmbedding(
        OpenAIClient provider,
        string modelId,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        return new EmbeddingGeneratorBuilder<string, Embedding<float>>(
                provider.GetEmbeddingClient(modelId).AsIEmbeddingGenerator())
            .UseOpenTelemetry(
                loggerFactory,
                DigitalBrainAIHosting.TelemetrySourceName,
                telemetry => telemetry.EnableSensitiveData = false)
            .Build();
    }
}
