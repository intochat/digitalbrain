using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Orleans;

namespace DigitalBrain.Aspire.Hosting;

public static class DigitalBrainHostingExtensions
{
    public static DigitalBrainBuilder AddDigitalBrain(
        this IDistributedApplicationBuilder builder,
        string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var resource = builder.AddResource(new DigitalBrainResource(name));
        var storage = builder
            .AddAzureStorage(DigitalBrainNames.Storage)
            .RunAsEmulator(static emulator => emulator
                .WithDataVolume()
                .WithLifetime(ContainerLifetime.Persistent));
        var clustering = storage.AddTables(DigitalBrainNames.Clustering);
        var reminders = storage.AddTables(DigitalBrainNames.Reminders);
        var grainState = storage.AddBlobs(DigitalBrainNames.GrainState);
        var journal = storage.AddBlobs(DigitalBrainNames.Journal);
        var orleans = builder
            .AddOrleans(name)
            .WithClustering(clustering)
            .WithReminders(reminders)
            .WithGrainStorage(DigitalBrainNames.DefaultGrainStorage, grainState);
        var brain = new DigitalBrainBuilder(builder, name, resource, orleans, grainState, journal);

        brain.RequireHealthyBeforeStart(storage.Resource);
        brain.RequireHealthyBeforeStart(clustering.Resource);
        brain.RequireHealthyBeforeStart(reminders.Resource);
        brain.RequireHealthyBeforeStart(grainState.Resource);
        brain.RequireHealthyBeforeStart(journal.Resource);
        return brain;
    }

    public static DigitalBrainBuilder AddModule<TModule>(this DigitalBrainBuilder brain)
        where TModule : class
        => brain.AddModule<TModule>(static _ => { });

    public static DigitalBrainBuilder AddModule<TModule>(
        this DigitalBrainBuilder brain,
        Action<DigitalBrainModuleBuilder<TModule>> configure)
        where TModule : class
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(configure);

        configure(new DigitalBrainModuleBuilder<TModule>(brain));
        return brain;
    }

    public static IResourceBuilder<TResource> WithReference<TResource>(
        this IResourceBuilder<TResource> builder,
        DigitalBrainBuilder brain)
        where TResource : IResourceWithEnvironment, IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(brain);

        builder.WithReference(brain.Orleans);
        builder.WithReference(brain.Journal, DigitalBrainNames.JournalConnection);
        WaitUntilHealthy(builder, brain.StartupDependencies);

        foreach (var projection in brain.Projections)
        {
            projection.ApplyToRuntime(builder);
        }

        return builder;
    }

    public static IResourceBuilder<TResource> WithReference<TResource>(
        this IResourceBuilder<TResource> builder,
        DigitalBrainClientReference client)
        where TResource : IResourceWithEnvironment, IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(client);

        builder.WithReference(client.Brain.Orleans.AsClient());
        WaitUntilHealthy(builder, client.Brain.StartupDependencies);

        foreach (var projection in client.Brain.Projections)
        {
            projection.ApplyToClient(builder);
        }

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
