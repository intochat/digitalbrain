namespace DigitalBrain.Features.Sdk;

public interface IFeatureContext
{
    IFeatureClock Clock { get; }
    IFeatureIdentifiers Identifiers { get; }
    IFeatureState State { get; }
    IMemoryRecall MemoryRecall { get; }
    IMemoryRemember MemoryRemember { get; }
    IModelWorkflow Models { get; }
    IFeatureIntentBuffer Intents { get; }
}

public interface IFeatureClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IFeatureIdentifiers
{
    string Next(string scope);
}

public interface IFeatureState
{
    FeatureState Read();
    void Replace(FeatureState state);
}

public sealed class FeatureState
{
    public FeatureState(string json)
    {
        Json = FeatureContractGuard.Json(json, nameof(json), 65_536);
    }

    public string Json { get; }
}

public interface IModelWorkflow
{
    Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default);
}

public sealed class ModelRequest
{
    public ModelRequest(string workflowId, string prompt, string logicalOperationKey)
    {
        WorkflowId = FeatureContractGuard.Required(workflowId, nameof(workflowId), 128);
        Prompt = FeatureContractGuard.Utf8(prompt, nameof(prompt), 32_768);
        LogicalOperationKey = FeatureContractGuard.Required(logicalOperationKey, nameof(logicalOperationKey), 256);
    }

    public string WorkflowId { get; }
    public string Prompt { get; }
    public string LogicalOperationKey { get; }
}

public sealed class ModelResponse
{
    public ModelResponse(string text)
    {
        Text = FeatureContractGuard.Utf8(text, nameof(text), 32_768);
    }

    public string Text { get; }
}

public interface IFeatureIntentBuffer
{
    void AddTextSurface(TextSurfaceIntent intent);
    void EmitEvent(EventIntent intent);
    void ProposeExternalEffect(ExternalEffectIntent intent);
}

public sealed class TextSurfaceIntent
{
    public TextSurfaceIntent(string logicalOperationKey, string title, string text)
    {
        LogicalOperationKey = FeatureContractGuard.Required(logicalOperationKey, nameof(logicalOperationKey), 256);
        Title = FeatureContractGuard.Required(title, nameof(title), 256);
        Text = FeatureContractGuard.Utf8(text, nameof(text), 65_536);
    }

    public string LogicalOperationKey { get; }
    public string Title { get; }
    public string Text { get; }
}

public sealed class EventIntent
{
    public EventIntent(string logicalOperationKey, string schemaId, string json)
    {
        LogicalOperationKey = FeatureContractGuard.Required(logicalOperationKey, nameof(logicalOperationKey), 256);
        SchemaId = FeatureContractGuard.Required(schemaId, nameof(schemaId), 256);
        Json = FeatureContractGuard.Json(json, nameof(json), 65_536);
    }

    public string LogicalOperationKey { get; }
    public string SchemaId { get; }
    public string Json { get; }
}

public sealed class ExternalEffectIntent
{
    public ExternalEffectIntent(string logicalOperationKey, string capabilityId, string? providerConnectionId, string json)
    {
        LogicalOperationKey = FeatureContractGuard.Required(logicalOperationKey, nameof(logicalOperationKey), 256);
        CapabilityId = FeatureContractGuard.Required(capabilityId, nameof(capabilityId), 256);
        ProviderConnectionId = providerConnectionId is null ? null : FeatureContractGuard.Required(providerConnectionId, nameof(providerConnectionId), 256);
        Json = FeatureContractGuard.Json(json, nameof(json), 65_536);
    }

    public string LogicalOperationKey { get; }
    public string CapabilityId { get; }
    public string? ProviderConnectionId { get; }
    public string Json { get; }
}
