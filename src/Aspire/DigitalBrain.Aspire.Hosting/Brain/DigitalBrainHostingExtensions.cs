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
        return (brain.Storage ?? throw new InvalidOperationException("Blob containers require UseAzureStorage." )).AddBlobContainer(brain.ResourceName(name));
    }

    public static DigitalBrainBuilder AddDigitalBrain(this IDistributedApplicationBuilder builder, string name, bool persistentStorage = true, string? dataVolume = null, string? serviceId = null, DigitalBrainHostingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        options ??= new();
        var persist = persistentStorage && builder.Configuration.GetValue(DigitalBrainHostingNames.PersistentStorageKey, true);
        var stableServiceId = string.IsNullOrWhiteSpace(serviceId) ? name : serviceId;

        var resource = builder.AddResource(new DigitalBrainResource(name))
            .ExcludeFromManifest()
            .WithInitialState(new CustomResourceSnapshot
            {
                ResourceType = "Modules",
                CreationTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.Running,
                Properties = [new(CustomResourceKnownProperties.Source, "DigitalBrain modules")],
            });
        // A configured cluster id is one session (tests pass one id and join neither each other nor a previous run).
        // Development otherwise gets a new cluster so persistent membership does not resurrect dead silos.
        // The service id stays stable unless that session id was supplied, so grain storage survives the new cluster.
        var configuredClusterId = builder.Configuration["Orleans:ClusterId"];
        var clusterId = configuredClusterId
            ?? (builder.Environment.IsDevelopment() ? $"digitalbrain-{Guid.NewGuid():N}" : stableServiceId);
        var resolvedServiceId = builder.Configuration["Orleans:ServiceId"]
            ?? (configuredClusterId is not null ? clusterId : stableServiceId);
        var orleans = builder
            .AddOrleans($"{name}-{DigitalBrainHostingNames.Orleans}")
            .WithClusterId(clusterId)
            .WithServiceId(resolvedServiceId);
        if (!options.UseAzureStorage)
        {
            orleans.WithDevelopmentClustering().WithMemoryGrainStorage(DigitalBrainNames.DefaultGrainStorage).WithMemoryReminders();
            var memory = new DigitalBrainBuilder(builder, name, resource, orleans, null, null) { Dashboard = options.Dashboard };
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
        var brain = new DigitalBrainBuilder(builder, name, resource, orleans, storage, grainState) { Dashboard = options.Dashboard };
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

        builder.WithAnnotation(new BrainSiloAnnotation(brain.Name));
        builder.WithReference(brain.Orleans);
        if (brain.GrainState is { } grainState) { builder.WithReference(grainState, DigitalBrainNames.GrainState); }

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

        if (brain.Dashboard) { builder.WithUrlForEndpoint("http", endpoint => new ResourceUrlAnnotation
        {
            Url = DigitalBrainNames.OrleansDashboardPath,
            DisplayText = "Orleans Dashboard",
            Endpoint = endpoint,
        }); }

        return builder;
    }

    public static IResourceBuilder<TResource> WithReference<TResource>(this IResourceBuilder<TResource> builder, DigitalBrainClientReference client)
        where TResource : IResourceWithEnvironment, IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(client);

        builder.WithReference(client.Brain.Orleans.AsClient());
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
