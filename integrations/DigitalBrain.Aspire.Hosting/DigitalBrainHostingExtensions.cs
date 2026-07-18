using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.DigitalBrain;
using DigitalBrain;

namespace Aspire.Hosting;

public static class DigitalBrainHostingExtensions
{
    private const string NotificationStreamProviderName = "NeuronNotification";

    [AspireExport]
    public static IResourceBuilder<DigitalBrainResource> AddDigitalBrain(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var discoveryStorage = builder
            .AddAzureStorage($"{name}-discovery-storage")
            .RunAsEmulator();
        var clustering = discoveryStorage.AddTables($"{name}-clustering");
        var storage = builder
            .AddAzureStorage($"{name}-storage")
            .RunAsEmulator();
        var reminders = storage.AddTables($"{name}-reminders");
        var grainState = storage.AddBlobs($"{name}-grain-state");
        var journal = storage.AddBlobs($"{name}-journal");
        var streams = storage.AddQueues($"{name}-streams");
        var outbox = storage.AddQueues($"{name}-outbox");
        var clusterId = $"{name}-cluster";
        var serviceId = $"{name}-service";
        var orleans = builder
            .AddOrleans($"{name}-orleans")
            .WithClusterId(clusterId)
            .WithServiceId(serviceId)
            .WithClustering(clustering)
            .WithReminders(reminders)
            .WithGrainStorage("Default", grainState)
            .WithStreaming(NotificationStreamProviderName, streams);
        var clientOrleans = builder
            .AddOrleans($"{name}-orleans-client")
            .WithClusterId(clusterId)
            .WithServiceId(serviceId)
            .WithClustering(clustering);
        clientOrleans.EnableDistributedTracing = false;
        var resource = new DigitalBrainResource(
            name,
            builder,
            orleans,
            clientOrleans,
            discoveryStorage,
            storage,
            clustering,
            reminders,
            grainState,
            journal,
            streams,
            outbox);
        var brain = builder.AddResource(resource);

        discoveryStorage.WithParentRelationship(resource);
        storage.WithParentRelationship(resource);

        return brain;
    }

    [AspireExportIgnore(Reason = "Typed .NET model descriptors are not ATS generic arguments.")]
    public static DigitalBrainChatModelBuilder WithLLM<TModel>(
        this IResourceBuilder<DigitalBrainResource> brain)
        where TModel : ChatModelDescriptor, new()
    {
        ArgumentNullException.ThrowIfNull(brain);
        return new DigitalBrainChatModelBuilder(
            brain,
            brain.Resource.RegisterChat<TModel>());
    }

    [AspireExportIgnore(Reason = "Typed .NET model descriptors are not ATS generic arguments.")]
    public static IResourceBuilder<DigitalBrainResource> WithEmbedding<TModel>(
        this IResourceBuilder<DigitalBrainResource> brain)
        where TModel : EmbeddingModelDescriptor, new()
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.Resource.RegisterEmbedding<TModel>();
        return brain;
    }

    [AspireExport]
    public static DigitalBrainClientResource AsClient(
        this IResourceBuilder<DigitalBrainResource> brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        return new DigitalBrainClientResource(
            brain.Resource.Name,
            brain.Resource.ClientOrleans.AsClient(),
            brain.Resource.DiscoveryStorage);
    }

    [AspireExport("withDigitalBrainReference")]
    public static IResourceBuilder<T> WithReference<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<DigitalBrainResource> brain)
        where T : IResourceWithEnvironment, IResourceWithEndpoints, IResourceWithWaitSupport
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(brain);

        var configuration = brain.Resource.ModelConfiguration;
        builder.WithReference(brain.Resource.Orleans);
        builder.WithEnvironment(
            "ConnectionStrings__journal",
            brain.Resource.JournalStorage);
        builder.WithEnvironment(
            "DigitalBrain__Storage__Clustering",
            brain.Resource.ClusteringStorage);
        builder.WithEnvironment(
            "DigitalBrain__Storage__Reminders",
            brain.Resource.ReminderStorage);
        builder.WithEnvironment(
            "DigitalBrain__Storage__GrainState",
            brain.Resource.GrainStorage);
        builder.WithEnvironment(
            "DigitalBrain__Storage__Journal",
            brain.Resource.JournalStorage);
        builder.WithEnvironment(
            "DigitalBrain__Storage__Streams",
            brain.Resource.StreamStorage);
        builder.WithEnvironment(
            "DigitalBrain__Storage__Outbox",
            brain.Resource.OutboxStorage);
        builder.WithEnvironment(
            "DigitalBrain__AI__OpenAI__ApiKey",
            brain.Resource.OpenAIKey);
        builder.WithEnvironment(
            "DigitalBrain__AI__OpenAI__Endpoint",
            brain.Resource.OpenAI.Resource.Endpoint);
        builder.WithEnvironment(
            "DigitalBrain__AI__OpenAI__FastModelId",
            configuration.Fast.ModelId);
        builder.WithEnvironment(
            "DigitalBrain__AI__OpenAI__ReasoningModelId",
            configuration.Reasoning.ModelId);
        builder.WithEnvironment(
            "DigitalBrain__AI__OpenAI__EmbeddingModelId",
            configuration.Embedding.ModelId);
        builder.WithEnvironment(
            "DigitalBrain__AI__Anthropic__ApiKey",
            brain.Resource.AnthropicKey);
        builder.WithEnvironment(
            "DigitalBrain__AI__Anthropic__Endpoint",
            brain.Resource.Anthropic.Resource.Endpoint.AbsoluteUri);
        builder.WithEnvironment(
            "DigitalBrain__AI__Anthropic__BalancedModelId",
            configuration.Balanced.ModelId);
        builder.WaitFor(brain.Resource.DiscoveryStorage);
        builder.WaitFor(brain.Resource.ClusteringStorage);
        builder.WaitFor(brain.Resource.Storage);
        builder.WaitFor(brain.Resource.ReminderStorage);
        builder.WaitFor(brain.Resource.GrainStorage);
        builder.WaitFor(brain.Resource.JournalStorage);
        builder.WaitFor(brain.Resource.StreamStorage);
        builder.WaitFor(brain.Resource.OutboxStorage);
        builder.WaitFor(brain.Resource.OpenAI);

        return builder;
    }

    [AspireExport("withDigitalBrainClientReference")]
    public static IResourceBuilder<T> WithReference<T>(
        this IResourceBuilder<T> builder,
        DigitalBrainClientResource brain)
        where T : IResourceWithEnvironment, IResourceWithEndpoints, IResourceWithWaitSupport
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(brain);

        builder.WithReferenceEnvironment(
            ReferenceEnvironmentInjectionFlags.ConnectionString);
        builder.WithReference(brain.Orleans);
        builder.WithReferenceEnvironment(
            ReferenceEnvironmentInjectionFlags.All);
        builder.WithEnvironment("DigitalBrain__Client__Name", brain.Name);
        builder.WithEnvironment("DigitalBrain__Client__ContractVersion", "1");
        builder.WaitFor(brain.DiscoveryStorage);

        return builder;
    }
}
