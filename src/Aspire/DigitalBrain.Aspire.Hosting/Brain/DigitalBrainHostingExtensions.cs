using System.Reflection;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Aspire.Hosting;

public static class DigitalBrainHostingExtensions
{
    public static IResourceBuilder<AzureBlobStorageContainerResource> AddBlobContainer(this DigitalBrainBuilder brain, string name)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return brain.Storage.AddBlobContainer(name);
    }

    public static DigitalBrainBuilder AddModules(this DigitalBrainBuilder brain, IReadOnlyList<ModuleDefinition> modules)
    {
        // Code-declared module settings are defaults; the AppHost's own configuration (args, env,
        // files) wins by standard precedence, so a host or test overrides any option without a side channel.
        var resolved = ModuleComposition.Resolve(modules)
            .Select(module => Overlay(module, brain.ApplicationBuilder.Configuration)).ToArray();
        foreach (var module in resolved) { brain.SetModuleConfiguration(module); }
        var settings = resolved.SelectMany(m => m.Configuration).DistinctBy(p => p.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        brain.ApplicationBuilder.Configuration.AddInMemoryCollection(settings);
        brain.AddProjection(new ModuleSettingsProjection(settings));
        foreach (var module in resolved) { brain.AddModuleType(module.ModuleType); }
        return brain;
    }

    private static ModuleDefinition Overlay(ModuleDefinition module, IConfiguration configuration)
    {
        var overlaid = new Dictionary<string, string?>(module.Configuration, StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var key in module.Configuration.Keys)
        {
            if (configuration[key] is { } value && value != overlaid[key]) { overlaid[key] = value; changed = true; }
        }
        // Keys the host adds under the options section (new list or dictionary entries) have no
        // code-declared counterpart; merge every configured leaf, not just known keys.
        foreach (var (key, value) in Leaves(configuration.GetSection(ModuleOptionsSerialization.OptionsKey(module.ModuleType.Name))))
        {
            if (!overlaid.TryGetValue(key, out var existing) || existing != value) { overlaid[key] = value; changed = true; }
        }
        return changed ? new ModuleDefinition(module.ModuleType, overlaid, module.Dependencies) : module;
    }

    private static IEnumerable<KeyValuePair<string, string?>> Leaves(IConfigurationSection section)
    {
        var children = section.GetChildren().ToArray();
        if (children.Length == 0)
        {
            if (section.Value is not null) { yield return new(section.Path, section.Value); }
            yield break;
        }
        foreach (var child in children)
        {
            foreach (var leaf in Leaves(child)) { yield return leaf; }
        }
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
        brain.AddProjection(MasterKey.Provision(builder));
        if (AuthPosture.Provision(builder) is { } posture) { brain.AddProjection(posture); }
        var kernel = brain.GetOrAddModuleNode(DigitalBrainHostingNames.Kernel);
        var storage = builder
            .AddAzureStorage(DigitalBrainNames.Storage)
            .RunAsEmulator(emulator =>
            {
                emulator.WithArgs("--silent");
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
        brain.AttachRuntime(orleans, storage, grainState);

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
    // A module without that assembly has no hosting; an assembly that lacks the named type is a naming mistake and fails loudly.
    private static IDigitalBrainModuleHosting? ResolveModuleHosting(Type moduleType)
    {
        Assembly hostingAssembly;
        try { hostingAssembly = Assembly.Load(moduleType.Assembly.GetName().Name + ".Aspire.Hosting"); }
        catch (FileNotFoundException) { return null; }
        return FindModuleHosting(moduleType, hostingAssembly);
    }

    public static IDigitalBrainModuleHosting FindModuleHosting(Type moduleType, Assembly hostingAssembly)
    {
        ArgumentNullException.ThrowIfNull(moduleType);
        ArgumentNullException.ThrowIfNull(hostingAssembly);
        var expectedName = moduleType.Name + "Hosting";
        var hostingType = hostingAssembly.GetTypes().SingleOrDefault(type =>
            type is { IsAbstract: false } && typeof(IDigitalBrainModuleHosting).IsAssignableFrom(type) && type.Name == expectedName)
            ?? throw new InvalidOperationException(
                $"{hostingAssembly.GetName().Name} has no {nameof(IDigitalBrainModuleHosting)} named {expectedName} for {moduleType.Name}.");
        return (IDigitalBrainModuleHosting)Activator.CreateInstance(hostingType)!;
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
