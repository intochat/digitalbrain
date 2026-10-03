using System.Reflection;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using DigitalBrain;
using DigitalBrain.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Aspire.Hosting;

public static class DigitalBrainHostingExtensions
{
    public static IResourceBuilder<AzureBlobStorageContainerResource> AddBlobContainer(this DigitalBrainBuilder brain, string name)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return (brain.Storage ?? throw new InvalidOperationException("Blob containers require UseAzureStorage."))
            .AddBlobContainer(brain.ResourceName(name), blobContainerName: name);
    }

    public static DigitalBrainBuilder AddDigitalBrain(this IDistributedApplicationBuilder builder, string name, bool persistentStorage = true, string? dataVolume = null, string? serviceId = null, DigitalBrainHostingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        options ??= new();
        var persist = persistentStorage && builder.Configuration.GetValue(DigitalBrainHostingNames.PersistentStorageKey, true);
        if (serviceId is not null && options.ServiceId is not null && serviceId != options.ServiceId)
        { throw new ArgumentException("Specify one consistent service ID.", nameof(serviceId)); }
        var resolvedServiceId = options.ServiceId ?? serviceId ?? builder.Configuration["Orleans:ServiceId"] ?? name;
        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedServiceId);

        var resource = builder.AddResource(new DigitalBrainResource(name))
            .ExcludeFromManifest()
            .WithInitialState(new CustomResourceSnapshot
            {
                ResourceType = "Modules",
                CreationTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.Running,
                Properties = [new(CustomResourceKnownProperties.Source, "DigitalBrain modules")],
            });
        // Cluster membership may change between runs; persistence identity must not.
        var clusterId = options.ClusterId ?? builder.Configuration["Orleans:ClusterId"]
            ?? (builder.Environment.IsDevelopment() ? $"digitalbrain-{Guid.NewGuid():N}" : resolvedServiceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clusterId);
        var orleans = builder
            .AddOrleans($"{name}-{DigitalBrainHostingNames.Orleans}")
            .WithClusterId(clusterId)
            .WithServiceId(resolvedServiceId);
        if (!options.UseAzureStorage)
        {
            orleans.WithDevelopmentClustering().WithMemoryGrainStorage(DigitalBrainNames.DefaultGrainStorage).WithMemoryReminders();
            var memory = new DigitalBrainBuilder(builder, name, resource, orleans, null, null) { Dashboard = options.Dashboard, ServiceId = resolvedServiceId, ClusterId = clusterId };
            memory.AddProjection(MasterKey.Provision(builder, name));
            if (AuthPosture.Provision(builder) is { } memoryPosture) { memory.AddProjection(memoryPosture); }
            return memory;
        }
        var storage = builder
            .AddAzureStorage($"{name}-{DigitalBrainNames.Storage}")
            .RunAsEmulator(emulator =>
            {
                emulator.WithArgs("--silent");
                if (persist) { emulator.WithLifetime(ContainerLifetime.Persistent); }
                if (dataVolume is not null) { emulator.WithDataVolume(dataVolume); }
                else if (persist) { emulator.WithDataVolume(); }
            })
            .WithParentRelationship(resource);
        var clustering = storage.AddTables($"{name}-{DigitalBrainNames.Clustering}");
        var reminders = storage.AddTables($"{name}-{DigitalBrainNames.Reminders}");
        var grainState = storage.AddBlobs($"{name}-{DigitalBrainNames.GrainState}");
        orleans.WithClustering(clustering).WithReminders(reminders)
            .WithGrainStorage(DigitalBrainNames.DefaultGrainStorage, grainState);
        var brain = new DigitalBrainBuilder(builder, name, resource, orleans, storage, grainState) { Dashboard = options.Dashboard, ServiceId = resolvedServiceId, ClusterId = clusterId, ClusteringResourceName = clustering.Resource.Name, Clustering = clustering, Reminders = reminders };
        brain.AddProjection(MasterKey.Provision(builder, name));
        if (AuthPosture.Provision(builder) is { } posture) { brain.AddProjection(posture); }
        storage.WithParentRelationship(brain.GetOrAddModuleNode(DigitalBrainHostingNames.Kernel));
        brain.RequireHealthyBeforeStart(storage.Resource);
        brain.RequireHealthyBeforeStart(clustering.Resource);
        brain.RequireHealthyBeforeStart(reminders.Resource);
        brain.RequireHealthyBeforeStart(grainState.Resource);
        return brain;
    }

    public static IResourceBuilder<TResource> WithReference<TResource>(this IResourceBuilder<TResource> builder, DigitalBrainBuilder brain)
        where TResource : IResourceWithEnvironment, IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(brain);

        builder.WithAnnotation(new BrainSiloAnnotation(brain));
        builder.WithReference(brain.Orleans);
        // Development clustering advertises localhost gateways. Local executables must bind
        // that address too, rather than Orleans selecting a VPN/container adapter address.
        if (brain.Clustering is null && builder.Resource is ProjectResource or ExecutableResource)
        {
            builder.WithEnvironment("Orleans__Endpoints__AdvertisedIPAddress", "127.0.0.1");
            // Orleans system-target addresses carry the real port; a TCP proxy port cannot
            // substitute for it when a static client requests the cluster manifest.
            builder.WithEndpoint("orleans-silo", endpoint => endpoint.IsProxied = false);
            builder.WithEndpoint("orleans-gateway", endpoint => endpoint.IsProxied = false);
        }
        if (brain.Clustering is { } clustering)
        {
            builder.WithReference(clustering, DigitalBrainNames.Clustering);
            builder.WithEnvironment("Orleans__Clustering__ServiceKey", DigitalBrainNames.Clustering);
        }
        if (brain.Reminders is { } reminders)
        {
            builder.WithReference(reminders, DigitalBrainNames.Reminders);
            builder.WithEnvironment("Orleans__Reminders__ServiceKey", DigitalBrainNames.Reminders);
        }
        if (brain.GrainState is { } grainState)
        {
            builder.WithReference(grainState, DigitalBrainNames.GrainState);
            builder.WithEnvironment($"Orleans__GrainStorage__{DigitalBrainNames.DefaultGrainStorage}__ServiceKey", DigitalBrainNames.GrainState);
        }
        brain.Identity?.Apply(builder, brain);

        for (var index = 0; index < brain.Modules.Count; index++)
        {
            var module = brain.Modules[index];
            builder.WithEnvironment(
                $"DigitalBrain__Modules__{module}__Enabled", "true");
        }

        foreach (var pair in brain.Settings)
        { builder.WithEnvironment(pair.Key.Replace(":", "__", StringComparison.Ordinal), pair.Value ?? ""); }

        WaitUntilHealthy(builder, brain.StartupDependencies);

        foreach (var projection in brain.Projections)
        {
            projection.Apply(builder);
        }

        if (brain.Dashboard)
        {
            builder.WithUrlForEndpoint("http", endpoint => new ResourceUrlAnnotation
            {
                Url = DigitalBrainNames.OrleansDashboardPath,
                DisplayText = "Orleans Dashboard",
                Endpoint = endpoint,
            });
        }

        return builder;
    }

    public static IResourceBuilder<TResource> WithReference<TResource>(this IResourceBuilder<TResource> builder, DigitalBrainClientReference client)
        where TResource : IResourceWithEnvironment, IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(client);

        builder.WithReference(client.Brain.Orleans.AsClient());
        AuthPosture.Provision(client.Brain.ApplicationBuilder)?.Apply(builder);
        if (client.ShareHttpIdentity)
        {
            var identity = client.Brain.Identity ?? throw new InvalidOperationException("Configure the brain HTTP identity before sharing sessions.");
            var storage = client.Brain.GrainState ?? throw new InvalidOperationException("Shared HTTP sessions require durable key storage.");
            identity.Apply(builder, client.Brain);
            builder.WithReference(storage, DigitalBrainNames.GrainState);
        }
        if (client.Brain.Clustering is { } clustering)
        {
            builder.WithReference(clustering, DigitalBrainNames.Clustering);
            builder.WithEnvironment("Orleans__Clustering__ServiceKey", DigitalBrainNames.Clustering);
        }
        WaitUntilHealthy(builder, client.Brain.StartupDependencies);
        return builder;
    }

    private static void WaitUntilHealthy<TResource>(
        IResourceBuilder<TResource> builder,
        IReadOnlyList<IResource> dependencies)
        where TResource : IResourceWithEnvironment, IResourceWithEndpoints
    {
        foreach (var dependency in dependencies)
        {
            builder.WithAnnotation(new WaitAnnotation(dependency, WaitType.WaitUntilHealthy, exitCode: 0));
        }
    }
}
