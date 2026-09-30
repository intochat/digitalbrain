using System.Reflection;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Aspire.Hosting;

public static class DigitalBrainHostingExtensions
{
    public static DigitalBrainBuilder AddModules(this DigitalBrainBuilder brain, IReadOnlyList<ModuleDefinition> modules)
    {
        var resolved = ModuleComposition.Resolve(modules);
        foreach (var module in resolved) { brain.SetModuleConfiguration(module); }
        var settings = resolved.SelectMany(m => m.Configuration).DistinctBy(p => p.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        brain.ApplicationBuilder.Configuration.AddInMemoryCollection(settings);
        brain.AddProjection(new ModuleSettingsProjection(settings));
        foreach (var module in resolved) { brain.AddModuleType(module.ModuleType); }
        return brain;
    }

    private sealed class ModuleSettingsProjection(KeyValuePair<string, string?>[] settings) : DigitalBrainModuleProjection
    {
        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            foreach (var pair in settings)
            { builder.WithEnvironment(pair.Key.Replace(":", "__", StringComparison.Ordinal), pair.Value ?? ""); }
        }
    }

    public static DigitalBrainBuilder AddDigitalBrain(this IDistributedApplicationBuilder builder, string name, bool persistentStorage = true, string? dataVolume = null, string? serviceId = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
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
        var brain = new DigitalBrainBuilder(builder, name, resource);
        var kernel = brain.GetOrAddModuleNode(DigitalBrainHostingNames.Kernel);
        var storage = builder
            .AddAzureStorage(DigitalBrainNames.Storage)
            .RunAsEmulator(emulator =>
            {
                if (persist) { emulator.WithLifetime(ContainerLifetime.Persistent); }
                if (dataVolume is not null) { emulator.WithDataVolume(dataVolume); }
                else if (persist) { emulator.WithDataVolume(); }
            })
            .WithParentRelationship(kernel);
        var clustering = storage.AddTables(DigitalBrainNames.Clustering);
        var reminders = storage.AddTables(DigitalBrainNames.Reminders);
        var grainState = storage.AddBlobs(DigitalBrainNames.GrainState);
        // A configured cluster id is one session (tests pass one id and join neither each other nor a previous run).
        // Development otherwise gets a new cluster so persistent membership does not resurrect dead silos.
        // The service id stays stable unless that session id was supplied, so grain storage survives the new cluster.
        var configuredClusterId = builder.Configuration["Orleans:ClusterId"];
        var clusterId = configuredClusterId
            ?? (builder.Environment.IsDevelopment() ? $"digitalbrain-{Guid.NewGuid():N}" : stableServiceId);
        var resolvedServiceId = builder.Configuration["Orleans:ServiceId"]
            ?? (configuredClusterId is not null ? clusterId : stableServiceId);
        var orleans = builder
            .AddOrleans(DigitalBrainHostingNames.Orleans)
            .WithClustering(clustering)
            .WithReminders(reminders)
            .WithGrainStorage(DigitalBrainNames.DefaultGrainStorage, grainState)
            .WithClusterId(clusterId)
            .WithServiceId(resolvedServiceId);
        brain.AttachRuntime(orleans, grainState);

        brain.RequireHealthyBeforeStart(storage.Resource);
        brain.RequireHealthyBeforeStart(clustering.Resource);
        brain.RequireHealthyBeforeStart(reminders.Resource);
        brain.RequireHealthyBeforeStart(grainState.Resource);
        return brain;
    }

    public static DigitalBrainBuilder AddModuleType(this DigitalBrainBuilder brain, Type moduleType)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(moduleType);
        brain.AddModule(moduleType);
        if (ResolveModuleHosting(moduleType) is { } defaults) { defaults.Configure(brain); }
        var nodeName = DigitalBrainHostingNames.ForModule(moduleType);
        if (!brain.HasResource(nodeName))
        {
            brain.GetOrAddModuleNode(nodeName);
        }

        return brain;
    }

    // A module's hosting adapter lives in the sibling "<ModuleAssembly>.Aspire.Hosting" assembly (which references the
    // module, so the module cannot name it) as the IDigitalBrainModuleHosting called "<ModuleTypeName>Hosting".
    private static IDigitalBrainModuleHosting? ResolveModuleHosting(Type moduleType)
    {
        Assembly hostingAssembly;
        try { hostingAssembly = Assembly.Load(moduleType.Assembly.GetName().Name + ".Aspire.Hosting"); }
        catch (FileNotFoundException) { return null; }
        var hostingType = hostingAssembly.GetTypes().SingleOrDefault(type =>
            type is { IsAbstract: false } && typeof(IDigitalBrainModuleHosting).IsAssignableFrom(type) && type.Name == moduleType.Name + "Hosting");
        return hostingType is null ? null : (IDigitalBrainModuleHosting)Activator.CreateInstance(hostingType)!;
    }

    public static IResourceBuilder<TResource> WithReference<TResource>(this IResourceBuilder<TResource> builder, DigitalBrainBuilder brain)
        where TResource : IResourceWithEnvironment, IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(brain);

        brain.Materialize();
        builder.WithReference(brain.Orleans);
        builder.WithReference(brain.GrainState, DigitalBrainNames.GrainState);

        for (var index = 0; index < brain.Modules.Count; index++)
        {
            var module = brain.Modules[index];
            builder.WithEnvironment(
                $"DigitalBrain__Modules__{index}",
                $"{module.FullName}, {module.Assembly.GetName().Name}");
        }

        WaitUntilHealthy(builder, brain.StartupDependencies);

        foreach (var projection in brain.Projections)
        {
            projection.Apply(builder);
        }

        builder.WithUrlForEndpoint("http", endpoint => new ResourceUrlAnnotation
        {
            Url = DigitalBrainNames.OrleansDashboardPath,
            DisplayText = "Orleans Dashboard",
            Endpoint = endpoint,
        });

        return builder;
    }

    public static IResourceBuilder<TResource> WithReference<TResource>(this IResourceBuilder<TResource> builder, DigitalBrainClientReference client)
        where TResource : IResourceWithEnvironment, IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(client);

        client.Brain.Materialize();
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