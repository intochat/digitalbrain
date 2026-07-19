using System.ClientModel;
using Anthropic;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

namespace DigitalBrain.Kernel;

public sealed record ModelDescriptor(ModelTier Tier, string Provider, string ModelId)
{
    public const string OpenAiProvider = ModelProviders.OpenAi;

    public const string AnthropicProvider = ModelProviders.Anthropic;

    public string? ApiKey { get; init; }

    public Uri? Endpoint { get; init; }

    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"Tier = {Tier}, Provider = {Provider}, ModelId = {ModelId}, Endpoint = {Endpoint}");

        return true;
    }
}

public sealed class ModelCatalog
{
    private readonly Dictionary<ModelTier, ModelDescriptor> _byTier = [];

    public IReadOnlyCollection<ModelDescriptor> Declared => _byTier.Values;

    public ModelCatalog Declare(ModelDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ModelId);

        if (!_byTier.TryAdd(descriptor.Tier, descriptor))
        {
            throw new InvalidOperationException(
                $"The {descriptor.Tier} tier is already bound to {_byTier[descriptor.Tier].ModelId}. Each tier binds to exactly one model.");
        }

        return this;
    }
}

public static class ModelBindingExtensions
{
    public static IServiceCollection AddDigitalBrainModels(this IServiceCollection services, Action<ModelCatalog> declare)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(declare);

        var catalog = new ModelCatalog();
        declare(catalog);

        foreach (var descriptor in catalog.Declared)
        {
            var bound = descriptor;

            services.AddKeyedChatClient(bound.Tier, _ => ProviderFactory.Create(bound)).UseOpenTelemetry();
        }

        return services;
    }
}

internal static class ProviderFactory
{
    internal static IChatClient Create(ModelDescriptor descriptor) => descriptor.Provider switch
    {
        ModelDescriptor.OpenAiProvider => CreateOpenAi(descriptor),
        ModelDescriptor.AnthropicProvider => CreateAnthropic(descriptor),
        _ => throw new InvalidOperationException(
            $"'{descriptor.Provider}' is not a known model provider. Declare {ModelDescriptor.OpenAiProvider} or {ModelDescriptor.AnthropicProvider}."),
    };

    private static IChatClient CreateOpenAi(ModelDescriptor descriptor)
    {
        var options = new OpenAIClientOptions();

        if (descriptor.Endpoint is { } endpoint)
        {
            options.Endpoint = endpoint;
        }

        return new OpenAIClient(new ApiKeyCredential(RequiredKey(descriptor)), options)
            .GetChatClient(descriptor.ModelId)
            .AsIChatClient();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The provider client's lifetime is the returned IChatClient's, which the container holds as a keyed singleton for the life of the silo.")]
    private static IChatClient CreateAnthropic(ModelDescriptor descriptor)
    {
        var client = descriptor.Endpoint is { } endpoint
            ? new AnthropicClient { ApiKey = RequiredKey(descriptor), BaseUrl = endpoint.ToString() }
            : new AnthropicClient { ApiKey = RequiredKey(descriptor) };

        return client.AsIChatClient(descriptor.ModelId);
    }

    private static string RequiredKey(ModelDescriptor descriptor) => descriptor.ApiKey
        ?? throw new InvalidOperationException(
            $"The {descriptor.Tier} tier is bound to {descriptor.Provider}/{descriptor.ModelId} but no API key was supplied. Provider credentials are AppHost configuration.");
}
