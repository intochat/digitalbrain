using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.OpenAI;
using Aspire.Hosting.Orleans;
using Aspire.Hosting.Publishing;
using DigitalBrain;

namespace Aspire.Hosting.DigitalBrain;

[AspireExport]
public sealed class DigitalBrainResource : Resource
{
    private static readonly Uri DefaultOpenAIEndpoint =
        new("https://api.openai.com/v1", UriKind.Absolute);

    private static readonly Uri DefaultAnthropicEndpoint =
        new("https://api.anthropic.com", UriKind.Absolute);

    private readonly IDistributedApplicationBuilder _applicationBuilder;
    private readonly DigitalBrainModelConfigurationBuilder _configuration = new();
    private readonly Dictionary<string, IResourceBuilder<OpenAIModelResource>> _openAIModels =
        new(StringComparer.Ordinal);
    private IResourceBuilder<OpenAIResource>? _openAI;
    private IResourceBuilder<ParameterResource>? _openAIKey;
    private Uri? _openAIEndpoint;
    private IResourceBuilder<AnthropicResource>? _anthropic;
    private IResourceBuilder<ParameterResource>? _anthropicKey;

    internal DigitalBrainResource(
        string name,
        IDistributedApplicationBuilder applicationBuilder,
        OrleansService orleans,
        OrleansService clientOrleans,
        IResourceBuilder<AzureStorageResource> discoveryStorage,
        IResourceBuilder<AzureStorageResource> storage,
        IResourceBuilder<AzureTableStorageResource> clusteringStorage,
        IResourceBuilder<AzureTableStorageResource> reminderStorage,
        IResourceBuilder<AzureBlobStorageResource> grainStorage,
        IResourceBuilder<AzureBlobStorageResource> journalStorage,
        IResourceBuilder<AzureQueueStorageResource> streamStorage,
        IResourceBuilder<AzureQueueStorageResource> outboxStorage)
        : base(name)
    {
        _applicationBuilder = applicationBuilder;
        Orleans = orleans;
        ClientOrleans = clientOrleans;
        DiscoveryStorage = discoveryStorage;
        Storage = storage;
        ClusteringStorage = clusteringStorage;
        ReminderStorage = reminderStorage;
        GrainStorage = grainStorage;
        JournalStorage = journalStorage;
        StreamStorage = streamStorage;
        OutboxStorage = outboxStorage;
        Annotations.Add(new ManifestPublishingCallbackAnnotation(WriteToManifest));
    }

    public OrleansService Orleans { get; }

    public OrleansService ClientOrleans { get; }

    public IResourceBuilder<AzureStorageResource> DiscoveryStorage { get; }

    public IResourceBuilder<AzureStorageResource> Storage { get; }

    public IResourceBuilder<AzureTableStorageResource> ClusteringStorage { get; }

    public IResourceBuilder<AzureTableStorageResource> ReminderStorage { get; }

    public IResourceBuilder<AzureBlobStorageResource> GrainStorage { get; }

    public IResourceBuilder<AzureBlobStorageResource> JournalStorage { get; }

    public IResourceBuilder<AzureQueueStorageResource> StreamStorage { get; }

    public IResourceBuilder<AzureQueueStorageResource> OutboxStorage { get; }

    public DigitalBrainModelConfiguration ModelConfiguration => _configuration.Build();

    internal IResourceBuilder<OpenAIResource> OpenAI =>
        _openAI ?? throw new InvalidOperationException("No OpenAI model is declared.");

    internal IResourceBuilder<ParameterResource> OpenAIKey =>
        _openAIKey ?? throw new InvalidOperationException("No OpenAI credential is declared.");

    internal IResourceBuilder<AnthropicResource> Anthropic =>
        _anthropic ?? throw new InvalidOperationException("No Anthropic model is declared.");

    internal IResourceBuilder<ParameterResource> AnthropicKey =>
        _anthropicKey ?? throw new InvalidOperationException("No Anthropic credential is declared.");

    internal ChatModelDescriptor RegisterChat<TModel>()
        where TModel : ChatModelDescriptor, new()
    {
        var descriptor = new TModel();
        RegisterModel(descriptor);
        return descriptor;
    }

    internal void RegisterEmbedding<TModel>()
        where TModel : EmbeddingModelDescriptor, new()
    {
        var descriptor = new TModel();
        RegisterModel(descriptor);
        _configuration.AssignEmbedding(descriptor);
    }

    internal void AssignFast(ChatModelDescriptor descriptor) =>
        _configuration.AssignFast(descriptor);

    internal void AssignBalanced(ChatModelDescriptor descriptor) =>
        _configuration.AssignBalanced(descriptor);

    internal void AssignReasoning(ChatModelDescriptor descriptor) =>
        _configuration.AssignReasoning(descriptor);

    private void RegisterModel(ModelDescriptor descriptor)
    {
        switch (descriptor.Provider)
        {
            case ModelProvider.OpenAI:
                RegisterOpenAIModel(descriptor);
                break;
            case ModelProvider.Anthropic when descriptor is ChatModelDescriptor:
                RegisterAnthropicModel(descriptor);
                break;
            default:
                throw new InvalidOperationException(
                    $"The {descriptor.Provider} provider does not support {descriptor.Capability} hosting.");
        }
    }

    private void RegisterOpenAIModel(ModelDescriptor descriptor)
    {
        var endpoint = descriptor.Endpoint ?? DefaultOpenAIEndpoint;
        if (_openAIEndpoint is not null && _openAIEndpoint != endpoint)
            throw new InvalidOperationException("OpenAI model endpoints must be consistent.");

        if (_openAI is null)
        {
            _openAI = _applicationBuilder
                .AddOpenAI($"{Name}-openai")
                .WithParentRelationship(this);
            _openAIKey = _applicationBuilder.CreateResourceBuilder(_openAI.Resource.Key);
        }

        if (descriptor.Endpoint is not null)
            _openAI.WithEndpoint(endpoint.AbsoluteUri);

        _openAIEndpoint = endpoint;

        var modelKey = $"{descriptor.Capability}:{descriptor.ModelId}";
        if (_openAIModels.ContainsKey(modelKey))
            return;

        _openAIModels.Add(
            modelKey,
            _openAI.AddModel(
                ResourceName($"{Name}-{descriptor.GetType().Name}"),
                descriptor.ModelId));
    }

    private void RegisterAnthropicModel(ModelDescriptor descriptor)
    {
        var endpoint = descriptor.Endpoint ?? DefaultAnthropicEndpoint;
        if (_anthropic is not null)
        {
            if (!string.Equals(
                    _anthropic.Resource.ModelId,
                    descriptor.ModelId,
                    StringComparison.Ordinal) ||
                _anthropic.Resource.Endpoint != endpoint)
                throw new InvalidOperationException(
                    "A DigitalBrain resource supports one Anthropic model declaration.");

            return;
        }

        _anthropicKey = _applicationBuilder.AddParameter(
            $"{Name}-anthropic-api-key",
            secret: true);
        var resource = new AnthropicResource(
            $"{Name}-anthropic",
            endpoint,
            _anthropicKey.Resource,
            descriptor.ModelId,
            this);
        _anthropic = _applicationBuilder
            .AddResource(resource)
            .WithParentRelationship(this);
        _anthropicKey.WithParentRelationship(resource);
    }

    private void WriteToManifest(ManifestPublishingContext context)
    {
        var configuration = ModelConfiguration;
        var writer = context.Writer;

        writer.WriteString("type", "digitalbrain.v0");
        writer.WriteString("orleans", Orleans.Name);
        writer.WriteString("clientOrleans", ClientOrleans.Name);
        writer.WriteStartObject("storage");
        writer.WriteString("discoveryAccount", DiscoveryStorage.Resource.Name);
        writer.WriteString("durabilityAccount", Storage.Resource.Name);
        writer.WriteString("clustering", ClusteringStorage.Resource.Name);
        writer.WriteString("reminders", ReminderStorage.Resource.Name);
        writer.WriteString("grainState", GrainStorage.Resource.Name);
        writer.WriteString("journal", JournalStorage.Resource.Name);
        writer.WriteString("streams", StreamStorage.Resource.Name);
        writer.WriteString("outbox", OutboxStorage.Resource.Name);
        writer.WriteEndObject();
        writer.WriteStartObject("providers");
        writer.WriteString("openAI", OpenAI.Resource.Name);
        writer.WriteString("anthropic", Anthropic.Resource.Name);
        writer.WriteEndObject();
        writer.WriteStartObject("models");
        writer.WriteString("fast", configuration.Fast.ModelId);
        writer.WriteString("balanced", configuration.Balanced.ModelId);
        writer.WriteString("reasoning", configuration.Reasoning.ModelId);
        writer.WriteString("embedding", configuration.Embedding.ModelId);
        writer.WriteEndObject();

        context.TryAddDependentResources(DiscoveryStorage.Resource);
        context.TryAddDependentResources(Storage.Resource);
        context.TryAddDependentResources(OpenAI.Resource);
        context.TryAddDependentResources(Anthropic.Resource);
    }

    private static string ResourceName(string value)
    {
        var normalized = new string(
            value.Select(character =>
                    char.IsAsciiLetterOrDigit(character)
                        ? char.ToLowerInvariant(character)
                        : '-')
                .ToArray());
        return string.Join(
            '-',
            normalized.Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
