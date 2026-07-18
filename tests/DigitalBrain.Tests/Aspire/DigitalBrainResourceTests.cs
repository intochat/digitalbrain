using System.Reflection;
using System.Text;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.DigitalBrain;
using Aspire.Hosting.OpenAI;
using Aspire.Hosting.Publishing;
using Aspire.Hosting.Testing;
using DigitalBrain;
using Xunit;

namespace DigitalBrain.Tests.Aspire;

public sealed class DigitalBrainResourceTests
{
    [Fact]
    public void AddDigitalBrain_composes_distinct_official_durable_resources()
    {
        var builder = AspireModelTestBuilder.Create();

        var brain = CompleteBrain(builder);

        Assert.Same(brain.Resource, Assert.Single(builder.Resources.OfType<DigitalBrainResource>()));
        Assert.Equal("brain-orleans", brain.Resource.Orleans.Name);
        Assert.Equal("brain-orleans-client", brain.Resource.ClientOrleans.Name);
        Assert.NotNull(brain.Resource.Orleans.Clustering);
        Assert.NotNull(brain.Resource.Orleans.Reminders);
        Assert.Contains("Default", brain.Resource.Orleans.GrainStorage.Keys);
        Assert.Contains("NeuronNotification", brain.Resource.Orleans.Streaming.Keys);
        Assert.NotNull(brain.Resource.ClientOrleans.Clustering);
        Assert.Null(brain.Resource.ClientOrleans.Reminders);
        Assert.Empty(brain.Resource.ClientOrleans.GrainStorage);
        Assert.Empty(brain.Resource.ClientOrleans.Streaming);

        var storageResources = new IResource[]
        {
            brain.Resource.ClusteringStorage.Resource,
            brain.Resource.ReminderStorage.Resource,
            brain.Resource.GrainStorage.Resource,
            brain.Resource.JournalStorage.Resource,
            brain.Resource.StreamStorage.Resource,
            brain.Resource.OutboxStorage.Resource
        };

        Assert.Equal(storageResources.Length, storageResources.Distinct().Count());
        Assert.Equal(storageResources.Length, storageResources.Select(resource => resource.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(storageResources, resource => Assert.Contains(resource, builder.Resources));
        Assert.IsType<AzureTableStorageResource>(brain.Resource.ClusteringStorage.Resource);
        Assert.IsType<AzureTableStorageResource>(brain.Resource.ReminderStorage.Resource);
        Assert.IsType<AzureBlobStorageResource>(brain.Resource.GrainStorage.Resource);
        Assert.IsType<AzureBlobStorageResource>(brain.Resource.JournalStorage.Resource);
        Assert.IsType<AzureQueueStorageResource>(brain.Resource.StreamStorage.Resource);
        Assert.IsType<AzureQueueStorageResource>(brain.Resource.OutboxStorage.Resource);
        Assert.NotSame(brain.Resource.GrainStorage.Resource, brain.Resource.JournalStorage.Resource);
    }

    [Fact]
    public void Client_discovery_uses_a_storage_account_isolated_from_privileged_state()
    {
        var builder = AspireModelTestBuilder.Create();

        var brain = CompleteBrain(builder);

        Assert.NotSame(
            brain.Resource.DiscoveryStorage.Resource,
            brain.Resource.Storage.Resource);
        Assert.Same(
            brain.Resource.DiscoveryStorage.Resource,
            ((IResourceWithParent<AzureStorageResource>)brain.Resource.ClusteringStorage.Resource).Parent);
        Assert.All(
            new IResource[]
            {
                brain.Resource.ReminderStorage.Resource,
                brain.Resource.GrainStorage.Resource,
                brain.Resource.JournalStorage.Resource,
                brain.Resource.StreamStorage.Resource,
                brain.Resource.OutboxStorage.Resource
            },
            resource => Assert.Same(
                brain.Resource.Storage.Resource,
                ((IResourceWithParent<AzureStorageResource>)resource).Parent));
    }

    [Fact]
    public void Typed_model_declarations_build_the_approved_configuration_and_provider_resources()
    {
        var builder = AspireModelTestBuilder.Create();

        var brain = CompleteBrain(builder);
        var configuration = brain.Resource.ModelConfiguration;

        Assert.IsType<GptFast>(configuration.Fast);
        Assert.IsType<ClaudeBalanced>(configuration.Balanced);
        Assert.IsType<GptReasoning>(configuration.Reasoning);
        Assert.IsType<TextEmbedding>(configuration.Embedding);
        Assert.Single(builder.Resources.OfType<OpenAIResource>());
        Assert.Equal(3, builder.Resources.OfType<OpenAIModelResource>().Count());
        var anthropic = Assert.Single(builder.Resources.OfType<AnthropicResource>());
        Assert.Equal(new ClaudeBalanced().ModelId, anthropic.ModelId);
        Assert.True(anthropic.ApiKey.Secret);
    }

    [Fact]
    public void OpenAI_models_reject_default_and_custom_endpoint_mixing_in_either_order()
    {
        var defaultFirstBuilder = AspireModelTestBuilder.Create();
        var defaultFirst = defaultFirstBuilder.AddDigitalBrain("default-first");
        defaultFirst.WithLLM<GptFast>();

        Assert.Throws<InvalidOperationException>(
            () => defaultFirst.WithLLM<CustomEndpointOpenAIModel>());

        var customFirstBuilder = AspireModelTestBuilder.Create();
        var customFirst = customFirstBuilder.AddDigitalBrain("custom-first");
        customFirst.WithLLM<CustomEndpointOpenAIModel>();

        Assert.Throws<InvalidOperationException>(
            () => customFirst.WithLLM<GptFast>());
    }

    [Fact]
    public void Public_resource_types_are_ATS_exported_and_anthropic_exposes_connection_properties()
    {
        Assert.NotNull(typeof(DigitalBrainResource).GetCustomAttribute<AspireExportAttribute>());
        Assert.NotNull(typeof(DigitalBrainClientResource).GetCustomAttribute<AspireExportAttribute>());
        Assert.NotNull(typeof(AnthropicResource).GetCustomAttribute<AspireExportAttribute>());

        var builder = AspireModelTestBuilder.Create();
        CompleteBrain(builder);
        var anthropic = Assert.Single(builder.Resources.OfType<AnthropicResource>());
        var properties = ((IResourceWithConnectionString)anthropic)
            .GetConnectionProperties()
            .Select(pair => pair.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Endpoint", "Key", "ModelName", "Uri"], properties);
    }

    [Fact]
    public void Incomplete_model_roles_fail_before_a_privileged_reference_is_created()
    {
        var builder = AspireModelTestBuilder.Create();
        var brain = builder.AddDigitalBrain("brain")
            .WithLLM<GptFast>().AsFast()
            .WithEmbedding<TextEmbedding>();
        var kernel = builder.AddContainer("kernel", "scratch");

        var error = Assert.Throws<InvalidOperationException>(() => kernel.WithReference(brain));

        Assert.Contains(nameof(ConversationRole.Balanced), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publish_manifest_describes_the_composite_without_secret_values()
    {
        var builder = AspireModelTestBuilder.Create();
        var brain = CompleteBrain(builder);
        var annotation = Assert.Single(
            brain.Resource.Annotations.OfType<ManifestPublishingCallbackAnnotation>());
        await using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            await annotation.Callback!(new ManifestPublishingContext(
                builder.ExecutionContext,
                "manifest.json",
                writer,
                CancellationToken.None));
            writer.WriteEndObject();
        }

        using var manifest = JsonDocument.Parse(stream.ToArray());
        var root = manifest.RootElement;

        Assert.Equal("digitalbrain.v0", root.GetProperty("type").GetString());
        Assert.Equal("brain-orleans", root.GetProperty("orleans").GetString());
        Assert.Equal("brain-orleans-client", root.GetProperty("clientOrleans").GetString());
        Assert.Equal("brain-journal", root.GetProperty("storage").GetProperty("journal").GetString());
        Assert.Equal("brain-grain-state", root.GetProperty("storage").GetProperty("grainState").GetString());
        Assert.Equal("gpt-5-mini", root.GetProperty("models").GetProperty("fast").GetString());
        Assert.Equal("claude-sonnet-4-5", root.GetProperty("models").GetProperty("balanced").GetString());
        Assert.Equal("gpt-5", root.GetProperty("models").GetProperty("reasoning").GetString());
        Assert.Equal("text-embedding-3-small", root.GetProperty("models").GetProperty("embedding").GetString());
        Assert.DoesNotContain(
            "apiKey",
            Encoding.UTF8.GetString(stream.ToArray()),
            StringComparison.OrdinalIgnoreCase);
    }

    private static IResourceBuilder<DigitalBrainResource> CompleteBrain(
        IDistributedApplicationBuilder builder) =>
        builder.AddDigitalBrain("brain")
            .WithLLM<GptFast>().AsFast()
            .WithLLM<ClaudeBalanced>().AsBalanced()
            .WithLLM<GptReasoning>().AsReasoning()
            .WithEmbedding<TextEmbedding>();

    private sealed record CustomEndpointOpenAIModel : ChatModelDescriptor
    {
        public CustomEndpointOpenAIModel()
            : base(ModelProvider.OpenAI, "custom-openai-model")
        {
            Endpoint = new Uri("https://openai-compatible.example/v1", UriKind.Absolute);
        }
    }
}
