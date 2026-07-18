using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orleans.Journaling;

namespace DigitalBrain.Kernel;

public static class DigitalBrainKernelHostingExtensions
{
    public static IHostApplicationBuilder AddDigitalBrainKernel(
        this IHostApplicationBuilder builder,
        string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var existing = builder.Services
            .FirstOrDefault(descriptor =>
                descriptor.ServiceType == typeof(DigitalBrainKernelRegistration))
            ?.ImplementationInstance as DigitalBrainKernelRegistration;
        if (existing is not null)
        {
            if (string.Equals(existing.Name, name, StringComparison.Ordinal))
                return builder;

            throw new InvalidOperationException(
                $"DigitalBrain kernel '{existing.Name}' is already registered; " +
                $"it cannot also be registered as '{name}'.");
        }

        var clusteringServiceKey = ServiceKey(
            builder.Configuration,
            "Orleans:Clustering:ServiceKey",
            $"{name}-clustering");
        var reminderServiceKey = ServiceKey(
            builder.Configuration,
            "Orleans:Reminders:ServiceKey",
            $"{name}-reminders");
        var grainStorageServiceKey = ServiceKey(
            builder.Configuration,
            "Orleans:GrainStorage:Default:ServiceKey",
            $"{name}-grain-state");
        var streamServiceKey = ServiceKey(
            builder.Configuration,
            "Orleans:Streaming:NeuronNotification:ServiceKey",
            $"{name}-streams");
        var journalServiceKey = $"{name}-journal";
        var outboxServiceKey = $"{name}-outbox";

        AddMissingConnectionStrings(
            builder.Configuration,
            new Dictionary<string, string?>
            {
                [clusteringServiceKey] =
                    builder.Configuration["DigitalBrain:Storage:Clustering"],
                [reminderServiceKey] =
                    builder.Configuration["DigitalBrain:Storage:Reminders"],
                [grainStorageServiceKey] =
                    builder.Configuration["DigitalBrain:Storage:GrainState"],
                [streamServiceKey] =
                    builder.Configuration["DigitalBrain:Storage:Streams"],
                [journalServiceKey] =
                    builder.Configuration["DigitalBrain:Storage:Journal"],
                [outboxServiceKey] =
                    builder.Configuration["DigitalBrain:Storage:Outbox"]
            });

        builder.Services.AddSingleton(new DigitalBrainKernelRegistration(name));
        builder.Services
            .AddOptions<DigitalBrainKernelOptions>()
            .Configure(options => options.Load(builder.Configuration, name))
            .ValidateOnStart();
        builder.Services.AddSingleton<
            IValidateOptions<DigitalBrainKernelOptions>,
            DigitalBrainKernelOptionsValidator>();

        AddTableClient(builder, clusteringServiceKey);
        AddTableClient(builder, reminderServiceKey, clusteringServiceKey);
        AddBlobClient(builder, grainStorageServiceKey);
        AddBlobClient(builder, journalServiceKey, grainStorageServiceKey);
        AddQueueClient(builder, streamServiceKey);
        AddQueueClient(builder, outboxServiceKey, streamServiceKey);
        builder.Services.AddSingleton(provider =>
            new DigitalBrainJournalBlobClient(
                provider.GetRequiredKeyedService<BlobServiceClient>(
                    journalServiceKey)));

        builder.UseOrleans(silo =>
        {
            silo.AddAzureBlobGrainStorage(
                "PubSubStore",
                options => options.Configure<IServiceProvider>(
                    (storage, services) =>
                        storage.BlobServiceClient =
                            services.GetRequiredKeyedService<BlobServiceClient>(
                                grainStorageServiceKey)));
            silo.AddAzureBlobJournalStorage(options =>
                options.ContainerName = $"{name}-journals");
            silo.Services
                .AddOptions<AzureBlobJournalStorageOptions>()
                .Configure<DigitalBrainJournalBlobClient>(
                    (options, client) =>
                        options.BlobServiceClient = client.Client);
            silo.AddBrainKernel();
            silo.AddDigitalBrainAI(builder.Configuration);
        });

        return builder;
    }

    private static string ServiceKey(
        IConfiguration configuration,
        string key,
        string registrationFallback)
    {
        var configured = configuration[key];
        return string.IsNullOrWhiteSpace(configured)
            ? registrationFallback
            : configured;
    }

    private static void AddTableClient(
        IHostApplicationBuilder builder,
        string serviceKey,
        string? existingServiceKey = null)
    {
        if (TryAddClientAlias<TableServiceClient>(
                builder.Services,
                serviceKey,
                existingServiceKey))
            return;

        if (DigitalBrainKernelOptionsValidator.IsStorageReference(
                builder.Configuration.GetConnectionString(serviceKey)))
        {
            builder.AddKeyedAzureTableServiceClient(serviceKey);
            return;
        }

        builder.Services.AddKeyedSingleton<TableServiceClient>(
            serviceKey,
            (provider, _) =>
                MissingClient<TableServiceClient>(provider, serviceKey));
    }

    private static void AddBlobClient(
        IHostApplicationBuilder builder,
        string serviceKey,
        string? existingServiceKey = null)
    {
        if (TryAddClientAlias<BlobServiceClient>(
                builder.Services,
                serviceKey,
                existingServiceKey))
            return;

        if (DigitalBrainKernelOptionsValidator.IsStorageReference(
                builder.Configuration.GetConnectionString(serviceKey)))
        {
            builder.AddKeyedAzureBlobServiceClient(serviceKey);
            return;
        }

        builder.Services.AddKeyedSingleton<BlobServiceClient>(
            serviceKey,
            (provider, _) =>
                MissingClient<BlobServiceClient>(provider, serviceKey));
    }

    private static void AddQueueClient(
        IHostApplicationBuilder builder,
        string serviceKey,
        string? existingServiceKey = null)
    {
        if (TryAddClientAlias<QueueServiceClient>(
                builder.Services,
                serviceKey,
                existingServiceKey))
            return;

        if (DigitalBrainKernelOptionsValidator.IsStorageReference(
                builder.Configuration.GetConnectionString(serviceKey)))
        {
            builder.AddKeyedAzureQueueServiceClient(serviceKey);
            return;
        }

        builder.Services.AddKeyedSingleton<QueueServiceClient>(
            serviceKey,
            (provider, _) =>
                MissingClient<QueueServiceClient>(provider, serviceKey));
    }

    private static bool TryAddClientAlias<TClient>(
        IServiceCollection services,
        string serviceKey,
        string? existingServiceKey)
        where TClient : class
    {
        if (!IsDuplicateServiceKey(serviceKey, existingServiceKey))
            return false;
        if (string.Equals(serviceKey, existingServiceKey, StringComparison.Ordinal))
            return true;

        services.AddKeyedSingleton<TClient>(
            serviceKey,
            (provider, _) =>
                provider.GetRequiredKeyedService<TClient>(
                    existingServiceKey!));
        return true;
    }

    private static bool IsDuplicateServiceKey(
        string serviceKey,
        string? existingServiceKey) =>
        existingServiceKey is not null
        && string.Equals(
            serviceKey,
            existingServiceKey,
            StringComparison.OrdinalIgnoreCase);

    private static TClient MissingClient<TClient>(
        IServiceProvider provider,
        string serviceKey)
    {
        _ = provider
            .GetRequiredService<IOptions<DigitalBrainKernelOptions>>()
            .Value;
        throw new InvalidOperationException(
            $"Azure Storage client '{serviceKey}' is not configured.");
    }

    private static void AddMissingConnectionStrings(
        IConfigurationManager configuration,
        IReadOnlyDictionary<string, string?> candidates)
    {
        Dictionary<string, string?> additions = new(StringComparer.Ordinal);
        foreach (var (serviceKey, value) in candidates)
        {
            if (string.IsNullOrWhiteSpace(
                    configuration.GetConnectionString(serviceKey))
                && !string.IsNullOrWhiteSpace(value))
            {
                additions[$"ConnectionStrings:{serviceKey}"] = value;
            }
        }

        if (additions.Count > 0)
            configuration.AddInMemoryCollection(additions);
    }

    private sealed class DigitalBrainJournalBlobClient(
        BlobServiceClient client)
    {
        internal BlobServiceClient Client { get; } = client;
    }
}
