using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

internal abstract class ApiKeyProviderFactory : ILlmProviderFactory
{
    public abstract AiProvider Provider { get; }

    public bool IsConfigured(AIOptions configuration, IAiCredentials credentials)
        => credentials.IsReady(IntegrationId);

    public virtual IChatClient CreateChatClient(LLMModel model, AIOptions configuration, IAiCredentials credentials)
        => CreateChatClient(model.Id, configuration, credentials);

    public abstract IChatClient CreateChatClient(string model, AIOptions configuration, IAiCredentials credentials, string? endpoint = null);

    public abstract IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        EmbeddingModel model,
        AIOptions configuration,
        IAiCredentials credentials);

    protected string IntegrationId => AiIntegrations.IdOf(Provider);

    // Releases inside the factory: the key exists in memory only for the duration of client construction.
    protected string ReleaseApiKey(IAiCredentials credentials)
    {
        credentials.RequireReady(IntegrationId);
        return credentials.ReleaseSecret(IntegrationId, AiIntegrations.ApiKeyField);
    }

    protected Uri EndpointOf(IAiCredentials credentials, string? pinned)
        => new(pinned is { Length: > 0 } ? pinned
            : credentials.Setting(IntegrationId, AiIntegrations.EndpointField) ?? AiIntegrations.DefaultEndpointOf(Provider));
}
