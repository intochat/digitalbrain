using Anthropic;
using Anthropic.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Kernel;

internal static class AnthropicProviderClientFactory
{
    public static IAnthropicClient CreateProvider(
        AnthropicProviderOptions options,
        HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var clientOptions = new ClientOptions
        {
            ApiKey = options.ApiKey!,
            BaseUrl = options.Endpoint!.AbsoluteUri.TrimEnd('/')
        };
        if (httpClient is not null)
            clientOptions = clientOptions with { HttpClient = httpClient };
        return new AnthropicClient(clientOptions);
    }

    public static IChatClient CreateChat(
        AnthropicProviderOptions options,
        string modelId,
        ILoggerFactory loggerFactory,
        HttpClient? httpClient = null) =>
        CreateChat(CreateProvider(options, httpClient), modelId, loggerFactory);

    public static IChatClient CreateChat(
        IAnthropicClient provider,
        string modelId,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        return new ChatClientBuilder(provider.AsIChatClient(modelId))
            .UseOpenTelemetry(
                loggerFactory,
                DigitalBrainAIHosting.TelemetrySourceName,
                telemetry => telemetry.EnableSensitiveData = false)
            .Build();
    }
}
