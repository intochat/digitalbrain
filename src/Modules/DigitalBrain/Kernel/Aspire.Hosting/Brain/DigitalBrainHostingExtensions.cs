using System.Reflection;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;

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

    public static DigitalBrainBuilder AddDigitalBrain(this IDistributedApplicationBuilder builder, string name, bool persistentStorage = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

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
                if (persistentStorage) { emulator.WithDataVolume().WithLifetime(ContainerLifetime.Persistent); }
            })
            .WithParentRelationship(kernel);
        var clustering = storage.AddTables(DigitalBrainNames.Clustering);
        var reminders = storage.AddTables(DigitalBrainNames.Reminders);
        var durableStateStore = storage.AddBlobs(DigitalBrainNames.Journal);
        var grainState = storage.AddBlobs(DigitalBrainNames.GrainState);
        var orleans = builder
            .AddOrleans(DigitalBrainHostingNames.Orleans)
            .WithClustering(clustering)
            .WithReminders(reminders)
            .WithGrainStorage(DigitalBrainNames.DefaultGrainStorage, grainState);
        brain.AttachRuntime(orleans, durableStateStore, grainState);

        brain.RequireHealthyBeforeStart(storage.Resource);
        brain.RequireHealthyBeforeStart(clustering.Resource);
        brain.RequireHealthyBeforeStart(reminders.Resource);
        brain.RequireHealthyBeforeStart(durableStateStore.Resource);
        brain.RequireHealthyBeforeStart(grainState.Resource);
        return brain;
    }

    public static DigitalBrainBuilder AddModuleType(this DigitalBrainBuilder brain, Type moduleType)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(moduleType);
        brain.AddModule(moduleType);
        var hostingAttribute = moduleType.GetCustomAttribute<ModuleHostingAttribute>();
        if (hostingAttribute is { } hosting)
        {
            var type = Type.GetType(hosting.TypeName, throwOnError: true)!;
            if (Activator.CreateInstance(type) is not IDigitalBrainModuleHosting defaults)
            {
                throw new InvalidOperationException($"{hosting.TypeName} must implement {nameof(IDigitalBrainModuleHosting)}.");
            }

            defaults.Configure(brain);
        }
        var nodeName = DigitalBrainHostingNames.ForModule(moduleType);
        if (!brain.HasResource(nodeName))
        {
            brain.GetOrAddModuleNode(nodeName);
        }

        return brain;
    }

    public static IResourceBuilder<TResource> WithReference<TResource>(this IResourceBuilder<TResource> builder, DigitalBrainBuilder brain)
        where TResource : IResourceWithEnvironment, IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(brain);

        brain.Materialize();
        builder.WithReference(brain.Orleans);
        builder.WithReference(brain.DurableStateStore, DigitalBrainNames.JournalConnection);
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