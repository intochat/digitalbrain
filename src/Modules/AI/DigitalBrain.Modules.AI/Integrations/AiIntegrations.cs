using DigitalBrain.Platform.Contracts.Integrations;

namespace DigitalBrain.AI;

internal static class AiIntegrations
{
    internal const string ApiKeyField = "ApiKey";
    internal const string EndpointField = "Endpoint";
    internal const string TavilyId = "tavily";

    internal static IntegrationDefinition[] Definitions { get; } =
    [
        Provider("openai", "OpenAI"),
        Provider("anthropic", "Anthropic"),
        Provider("google", "Google"),
        Provider("xai", "xAI"),
        IntegrationDefinition.For(TavilyId, "Tavily").RequiresSecret(ApiKeyField),
    ];

    internal static string IdOf(AiProvider provider) => provider switch
    {
        AiProvider.OpenAI => "openai",
        AiProvider.Anthropic => "anthropic",
        AiProvider.Google => "google",
        AiProvider.XAI => "xai",
        _ => throw new NotSupportedException($"{provider} is not a key-based provider."),
    };

    internal static string DefaultEndpointOf(AiProvider provider) => provider switch
    {
        AiProvider.OpenAI => "https://api.openai.com/v1",
        AiProvider.Anthropic => "https://api.anthropic.com",
        AiProvider.Google => "https://generativelanguage.googleapis.com/v1beta/openai/",
        AiProvider.XAI => "https://api.x.ai/v1",
        _ => throw new NotSupportedException($"{provider} has no hosted endpoint."),
    };

    internal static AiProvider[] KeyedProviders { get; } = [AiProvider.OpenAI, AiProvider.Anthropic, AiProvider.Google, AiProvider.XAI];

    private static IntegrationDefinition Provider(string id, string displayName)
        => IntegrationDefinition.For(id, displayName).RequiresSecret(ApiKeyField).OffersSetting(EndpointField);
}
