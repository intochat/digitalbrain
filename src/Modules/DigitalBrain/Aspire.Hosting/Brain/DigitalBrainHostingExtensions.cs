using System.Reflection;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace DigitalBrain.Aspire.Hosting;

public static class DigitalBrainHostingExtensions
{
    public static string DurableStateConnectionName => DigitalBrainNames.JournalConnection;

    public static DigitalBrainBuilder AddDigitalBrain(this IDistributedApplicationBuilder builder, string name)
        => builder.AddDigitalBrain(name, static options => { });

    public static DigitalBrainBuilder AddDigitalBrain(this IDistributedApplicationBuilder builder, string name,
        Action<DigitalBrainHostingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        var options = builder.Configuration.GetSection(DigitalBrainHostingOptions.SectionName)
            .Get<DigitalBrainHostingOptions>() ?? new();
        configure(options);
        if (options.StorageOperationBudget <= TimeSpan.Zero || options.RetryReminderPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentException("Neuron storage budget and retry reminder period must be positive.", nameof(configure));
        }

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
        if (options.StorageOperationBudget is not null || options.RetryReminderPeriod is not null)
        {
            brain.AddProjection(new NeuronConfigurationProjection(options.StorageOperationBudget, options.RetryReminderPeriod));
        }
        var kernel = brain.GetOrAddModuleNode(DigitalBrainHostingNames.Kernel);
        var storage = builder
            .AddAzureStorage(DigitalBrainNames.Storage)
            .RunAsEmulator(static emulator => emulator
                .WithDataVolume()
                .WithLifetime(ContainerLifetime.Persistent))
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

    public static DigitalBrainBuilder AddModule<TModule>(this DigitalBrainBuilder brain)
        where TModule : class
        => brain.AddModule<TModule>(static module => { });

    public static DigitalBrainBuilder AddModule<TModule>(this DigitalBrainBuilder brain, Action<DigitalBrainModuleBuilder<TModule>> configure)
        where TModule : class
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(configure);
        brain.AddModule(typeof(TModule));
        var module = new DigitalBrainModuleBuilder<TModule>(brain);
        var hostingAttribute = typeof(TModule).GetCustomAttribute<ModuleHostingAttribute>();
        if (hostingAttribute is { } hosting)
        {
            var type = Type.GetType(hosting.TypeName, throwOnError: true)!;
            if (Activator.CreateInstance(type) is not IDigitalBrainModuleHosting defaults)
            {
                throw new InvalidOperationException($"{hosting.TypeName} must implement {nameof(IDigitalBrainModuleHosting)}.");
            }

            defaults.Configure(brain);
        }
        configure(module);
        var nodeName = DigitalBrainHostingNames.ForModule(typeof(TModule));
        if (hostingAttribute is null && !brain.HasResource(nodeName))
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

    private sealed class NeuronConfigurationProjection(TimeSpan? storageBudget, TimeSpan? retryPeriod) : DigitalBrainModuleProjection
    {
        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (storageBudget is { } budget)
            {
                builder.WithEnvironment("DigitalBrain__Neuron__StorageOperationBudget", budget.ToString("c", CultureInfo.InvariantCulture));
            }
            if (retryPeriod is { } period)
            {
                builder.WithEnvironment("DigitalBrain__Neuron__RetryReminderPeriod", period.ToString("c", CultureInfo.InvariantCulture));
            }
        }
    }

}
