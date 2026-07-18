using Anthropic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;
using Orleans.Hosting;

namespace DigitalBrain.Kernel;

public static class DigitalBrainAIHosting
{
    internal const string TelemetrySourceName = "DigitalBrain.Neuron";

    public static ISiloBuilder AddDigitalBrainAI(
        this ISiloBuilder silo,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(silo);
        silo.Services.AddDigitalBrainAI(configuration);
        silo.Services.AddSingleton<
            IAttributeToFactoryMapper<ConversationStateAttribute>,
            ConversationStateMapper>();
        return silo;
    }

    internal static ISiloBuilder AddDigitalBrainAI(
        this ISiloBuilder silo,
        IConfiguration configuration,
        DigitalBrainAIHttpClients httpClients)
    {
        ArgumentNullException.ThrowIfNull(silo);
        silo.Services.AddDigitalBrainAI(configuration, httpClients);
        silo.Services.AddSingleton<
            IAttributeToFactoryMapper<ConversationStateAttribute>,
            ConversationStateMapper>();
        return silo;
    }

    internal static IServiceCollection AddDigitalBrainAI(
        this IServiceCollection services,
        IConfiguration configuration,
        DigitalBrainAIHttpClients? httpClients = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<OpenAIProviderOptions>()
            .Bind(configuration.GetSection(OpenAIProviderOptions.SectionName))
            .Validate(ValidateOpenAI, "Complete OpenAI endpoint, credential, and model configuration is required.")
            .ValidateOnStart();
        services
            .AddOptions<AnthropicProviderOptions>()
            .Bind(configuration.GetSection(AnthropicProviderOptions.SectionName))
            .Validate(ValidateAnthropic, "Complete Anthropic endpoint, credential, and model configuration is required.")
            .ValidateOnStart();

        var transports = httpClients ?? new DigitalBrainAIHttpClients();
        services.AddSingleton(transports);
        services.AddSingleton(sp =>
            OpenAIProviderClientFactory.CreateProvider(
                sp.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value,
                transports.OpenAI));
        services.AddSingleton<IAnthropicClient>(sp =>
            AnthropicProviderClientFactory.CreateProvider(
                sp.GetRequiredService<IOptions<AnthropicProviderOptions>>().Value,
                transports.Anthropic));
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value;
            return new FastModelClient(OpenAIProviderClientFactory.CreateChat(
                sp.GetRequiredService<OpenAIClient>(),
                options.FastModelId!,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()));
        });
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AnthropicProviderOptions>>().Value;
            return new BalancedModelClient(AnthropicProviderClientFactory.CreateChat(
                sp.GetRequiredService<IAnthropicClient>(),
                options.BalancedModelId!,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()));
        });
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value;
            return new ReasoningModelClient(OpenAIProviderClientFactory.CreateChat(
                sp.GetRequiredService<OpenAIClient>(),
                options.ReasoningModelId!,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()));
        });
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value;
            return new EmbeddingModelClient(OpenAIProviderClientFactory.CreateEmbedding(
                sp.GetRequiredService<OpenAIClient>(),
                options.EmbeddingModelId!,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()));
        });
        services.AddSingleton<IConversationRoleInvoker, ConversationRoleInvoker>();
        services
            .AddHealthChecks()
            .AddCheck<DigitalBrainAIHealthCheck>(DigitalBrainAIHealthCheck.Name);
        return services;
    }

    private static bool ValidateOpenAI(OpenAIProviderOptions options) =>
        HasText(options.ApiKey) &&
        IsHttpEndpoint(options.Endpoint) &&
        HasText(options.FastModelId) &&
        HasText(options.ReasoningModelId) &&
        HasText(options.EmbeddingModelId);

    private static bool ValidateAnthropic(AnthropicProviderOptions options) =>
        HasText(options.ApiKey) &&
        IsHttpEndpoint(options.Endpoint) &&
        HasText(options.BalancedModelId);

    private static bool HasText(string? value) =>
        !string.IsNullOrWhiteSpace(value);

    private static bool IsHttpEndpoint(Uri? endpoint) =>
        endpoint is { IsAbsoluteUri: true } &&
        string.IsNullOrEmpty(endpoint.UserInfo) &&
        (endpoint.Scheme == Uri.UriSchemeHttps ||
         endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback);
}
