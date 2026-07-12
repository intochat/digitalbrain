using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Orleans;
using DigitalBrain.Core.Models;

namespace DigitalBrain.Aspire;

public sealed class DigitalBrainContext
{
    public required string Name { get; init; }
    public required IDistributedApplicationBuilder ApplicationBuilder { get; init; }
    public required OrleansService Orleans { get; init; }
    public required IResourceBuilder<IResourceWithConnectionString> Llm { get; init; }
    public IResourceBuilder<IResourceWithConnectionString>? EmbeddingModel { get; init; }
    public required OrleansServiceClient OrleansClient { get; init; }
    public required int KernelReplicas { get; init; }
    public required DigitalBrainModelRegistry ModelRegistry { get; init; }

    public required string LlmModel { get; init; }
    public required string LlmProvider { get; init; }

    public EndpointReference? OllamaEndpoint { get; init; }
    public EndpointReference? EmbeddingOllamaEndpoint { get; init; }
    public IResourceBuilder<ParameterResource>? AzureOpenAIEndpoint { get; init; }
    public IResourceBuilder<ParameterResource>? AzureOpenAIKey { get; init; }
    public IResourceBuilder<ParameterResource>? OpenAIApiKey { get; init; }
    public IResourceBuilder<ParameterResource>? AnthropicApiKey { get; init; }
    public IResourceBuilder<ParameterResource>? GitHubModelsToken { get; init; }

    public required IResourceBuilder<AzureBlobStorageResource> GrainBlobs { get; init; }
    public required IResourceBuilder<AzureBlobStorageResource> ConversationStateBlobs { get; init; }
    public required IResourceBuilder<AzureBlobStorageResource> SurfaceFeedStateBlobs { get; init; }
    public required IResourceBuilder<AzureBlobStorageResource> SessionStateBlobs { get; init; }
    public required IResourceBuilder<AzureBlobStorageResource> JournalBlobs { get; init; }
    public required IResourceBuilder<AzureTableStorageResource> ClusteringTable { get; init; }

    public bool EnableMcp { get; init; }
}
