using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orleans.Clustering.AzureStorage;
using Orleans.Configuration;
using Orleans.Journaling;
using Orleans.Reminders.AzureStorage;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public sealed class DigitalBrainKernelHostingTests
{
    [Fact]
    public void Privileged_configuration_registers_the_official_durable_kernel()
    {
        var builder = CreateBuilder(CompleteConfigurationValues());

        Assert.Same(builder, builder.AddDigitalBrainKernel("brain"));

        using var host = builder.Build();
        var options = host.Services
            .GetRequiredService<IOptions<DigitalBrainKernelOptions>>()
            .Value;

        Assert.Equal("brain", options.Name);
        Assert.Equal("brain-clustering", options.ClusteringServiceKey);
        Assert.Equal("brain-reminders", options.ReminderServiceKey);
        Assert.Equal("brain-grain-state", options.GrainStorageServiceKey);
        Assert.Equal("brain-streams", options.StreamServiceKey);
        Assert.Equal("brain-journal", options.JournalServiceKey);
        Assert.Equal("brain-outbox", options.OutboxServiceKey);
        Assert.Equal("brain-journals", options.JournalContainerName);
        Assert.NotEqual(options.GrainStorageServiceKey, options.JournalServiceKey);

        Assert.NotNull(host.Services.GetRequiredKeyedService<TableServiceClient>("brain-clustering"));
        Assert.NotNull(host.Services.GetRequiredKeyedService<TableServiceClient>("brain-reminders"));
        Assert.NotNull(host.Services.GetRequiredKeyedService<BlobServiceClient>("brain-grain-state"));
        Assert.NotNull(host.Services.GetRequiredKeyedService<BlobServiceClient>("brain-journal"));
        Assert.NotNull(host.Services.GetRequiredKeyedService<QueueServiceClient>("brain-streams"));
        Assert.NotNull(host.Services.GetRequiredKeyedService<QueueServiceClient>("brain-outbox"));

        var clusteringClient =
            host.Services.GetRequiredKeyedService<TableServiceClient>("brain-clustering");
        var reminderClient =
            host.Services.GetRequiredKeyedService<TableServiceClient>("brain-reminders");
        var grainStorageClient =
            host.Services.GetRequiredKeyedService<BlobServiceClient>("brain-grain-state");
        var journalClient =
            host.Services.GetRequiredKeyedService<BlobServiceClient>("brain-journal");
        var streamClient =
            host.Services.GetRequiredKeyedService<QueueServiceClient>("brain-streams");
        Assert.Equal(
            "brain-cluster",
            host.Services.GetRequiredService<IOptions<ClusterOptions>>().Value.ClusterId);
        Assert.Equal(
            "brain-service",
            host.Services.GetRequiredService<IOptions<ClusterOptions>>().Value.ServiceId);
        Assert.Same(
            clusteringClient,
            host.Services
                .GetRequiredService<IOptions<AzureStorageClusteringOptions>>()
                .Value
                .TableServiceClient);
        Assert.Same(
            reminderClient,
            host.Services
                .GetRequiredService<IOptions<AzureTableReminderStorageOptions>>()
                .Value
                .TableServiceClient);
        var grainStorageOptions =
            host.Services.GetRequiredService<IOptionsSnapshot<AzureBlobStorageOptions>>();
        Assert.Same(
            grainStorageClient,
            grainStorageOptions.Get("Default").BlobServiceClient);
        Assert.Same(
            grainStorageClient,
            grainStorageOptions.Get("PubSubStore").BlobServiceClient);
        var journalOptions = host.Services
            .GetRequiredService<IOptions<AzureBlobJournalStorageOptions>>()
            .Value;
        Assert.Same(journalClient, journalOptions.BlobServiceClient);
        Assert.Equal("brain-journals", journalOptions.ContainerName);
        Assert.Same(
            streamClient,
            host.Services
                .GetRequiredService<IOptionsSnapshot<AzureQueueOptions>>()
                .Get(NeuronNotificationPublisher.StreamProviderName)
                .QueueServiceClient);
        var journalProvider =
            host.Services.GetRequiredService<IJournalStorageProvider>();
        Assert.Contains(
            "AzureBlobJournalStorageProvider",
            journalProvider.GetType().Name,
            StringComparison.Ordinal);

        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(IJournalStorageProvider));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(IConversationRoleInvoker));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(FastModelClient));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(BalancedModelClient));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(ReasoningModelClient));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(EmbeddingModelClient));
        Assert.IsNotType<VolatileJournalStorageProvider>(journalProvider);
    }

    [Fact]
    public async Task Missing_durable_storage_prevents_production_startup()
    {
        var configuration = CompleteConfigurationValues();
        configuration.Remove("DigitalBrain:Storage:Journal");
        var builder = CreateBuilder(configuration);
        builder.AddDigitalBrainKernel("brain");
        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var failure = await Assert.ThrowsAnyAsync<Exception>(
            () => host.StartAsync(timeout.Token));
        var validation = FindException<OptionsValidationException>(failure);

        Assert.True(validation is not null, failure.ToString());
        Assert.Contains("DigitalBrain:Storage:Journal", validation.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AccountName=first;AccountName=second;AccountKey=invalid")]
    [InlineData("DefaultEndpointsProtocol=https;AccountName=first;AccountKey=invalid;EndpointSuffix=core.windows.net")]
    [InlineData("DefaultEndpointsProtocol=banana;AccountName=first;AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=;EndpointSuffix=core.windows.net")]
    [InlineData("DefaultEndpointsProtocol=https;AccountName=first;AccountKey=AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=;EndpointSuffix=core.windows.net;TotallyUnknown=x")]
    public async Task Malformed_durable_storage_is_reported_by_startup_validation(
        string malformedStorage)
    {
        Assert.False(
            DigitalBrainKernelOptionsValidator.IsStorageReference(
                malformedStorage));
        var configuration = CompleteConfigurationValues();
        configuration["ConnectionStrings:brain-clustering"] = malformedStorage;
        var builder = CreateBuilder(configuration);

        builder.AddDigitalBrainKernel("brain");
        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var failure = await Assert.ThrowsAnyAsync<Exception>(
            () => host.StartAsync(timeout.Token));
        var validation = FindException<OptionsValidationException>(failure);

        Assert.True(validation is not null, failure.ToString());
        Assert.Contains(
            "ConnectionStrings:brain-clustering",
            validation.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ambient_storage_cannot_override_the_privileged_projection()
    {
        var configuration = CompleteConfigurationValues();
        configuration["ConnectionStrings:brain-journal"] =
            StorageConnectionString("ambientjournal");
        var builder = CreateBuilder(configuration);

        builder.AddDigitalBrainKernel("brain");
        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var failure = await Assert.ThrowsAnyAsync<Exception>(
            () => host.StartAsync(timeout.Token));
        var validation = FindException<OptionsValidationException>(failure);

        Assert.True(validation is not null, failure.ToString());
        Assert.Contains(
            "ConnectionStrings:brain-journal must match DigitalBrain:Storage:Journal",
            validation.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("brain-clustering")]
    [InlineData("BRAIN-CLUSTERING")]
    public async Task Reused_storage_service_keys_are_rejected_at_startup(
        string reminderServiceKey)
    {
        var configuration = CompleteConfigurationValues();
        configuration["Orleans:Reminders:ServiceKey"] = reminderServiceKey;
        var builder = CreateBuilder(configuration);

        builder.AddDigitalBrainKernel("brain");
        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var failure = await Assert.ThrowsAnyAsync<Exception>(
            () => host.StartAsync(timeout.Token));
        var validation = FindException<OptionsValidationException>(failure);

        Assert.True(validation is not null, failure.ToString());
        Assert.Contains(
            "storage service keys must be distinct",
            validation.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Registration_is_idempotent_for_one_name_and_rejects_a_conflicting_name()
    {
        var builder = CreateBuilder(CompleteConfigurationValues());
        builder.AddDigitalBrainKernel("brain");
        var descriptorCount = builder.Services.Count;

        Assert.Same(builder, builder.AddDigitalBrainKernel("brain"));
        Assert.Equal(descriptorCount, builder.Services.Count);

        var failure = Assert.Throws<InvalidOperationException>(
            () => builder.AddDigitalBrainKernel("other-brain"));
        Assert.Contains("brain", failure.Message, StringComparison.Ordinal);
        Assert.Contains("other-brain", failure.Message, StringComparison.Ordinal);
    }

    private static HostApplicationBuilder CreateBuilder(
        IReadOnlyDictionary<string, string?> configuration)
    {
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                Args = [],
                EnvironmentName = Environments.Production
            });
        builder.Configuration.AddInMemoryCollection(configuration);
        return builder;
    }

    private static Dictionary<string, string?> CompleteConfigurationValues()
    {
        var clustering = StorageConnectionString("brainclustering");
        var reminders = StorageConnectionString("brainreminders");
        var grainState = StorageConnectionString("braingrainstate");
        var journal = StorageConnectionString("brainjournal");
        var streams = StorageConnectionString("brainstreams");
        var outbox = StorageConnectionString("brainoutbox");
        var values = DigitalBrainAIRegistrationTests.CompleteConfigurationValues();

        values["Orleans:ClusterId"] = "brain-cluster";
        values["Orleans:ServiceId"] = "brain-service";
        values["Orleans:Clustering:ProviderType"] = "AzureTableStorage";
        values["Orleans:Clustering:ServiceKey"] = "brain-clustering";
        values["Orleans:Reminders:ProviderType"] = "AzureTableStorage";
        values["Orleans:Reminders:ServiceKey"] = "brain-reminders";
        values["Orleans:GrainStorage:Default:ProviderType"] = "AzureBlobStorage";
        values["Orleans:GrainStorage:Default:ServiceKey"] = "brain-grain-state";
        values["Orleans:Streaming:NeuronNotification:ProviderType"] = "AzureQueueStorage";
        values["Orleans:Streaming:NeuronNotification:ServiceKey"] = "brain-streams";
        values["ConnectionStrings:brain-clustering"] = clustering;
        values["ConnectionStrings:brain-reminders"] = reminders;
        values["ConnectionStrings:brain-grain-state"] = grainState;
        values["ConnectionStrings:brain-streams"] = streams;
        values["DigitalBrain:Storage:Clustering"] = clustering;
        values["DigitalBrain:Storage:Reminders"] = reminders;
        values["DigitalBrain:Storage:GrainState"] = grainState;
        values["DigitalBrain:Storage:Journal"] = journal;
        values["DigitalBrain:Storage:Streams"] = streams;
        values["DigitalBrain:Storage:Outbox"] = outbox;
        return values;
    }

    private static string StorageConnectionString(string accountName) =>
        $"DefaultEndpointsProtocol=https;AccountName={accountName};" +
        $"AccountKey={Convert.ToBase64String(new byte[32])};EndpointSuffix=core.windows.net";

    private static TException? FindException<TException>(Exception exception)
        where TException : Exception
    {
        if (exception is TException typed)
            return typed;

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.Flatten().InnerExceptions)
            {
                if (FindException<TException>(inner) is { } match)
                    return match;
            }
        }

        return exception.InnerException is { } innerException
            ? FindException<TException>(innerException)
            : null;
    }
}
