using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire.Hosting;

public static class DigitalBrainHostingExtensions
{
    public static string DurableStateConnectionName => DigitalBrainNames.JournalConnection;

    public static DigitalBrainBuilder AddDigitalBrain(this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var storage = builder
            .AddAzureStorage(DigitalBrainNames.Storage)
            .RunAsEmulator(static emulator => emulator
                .WithDataVolume()
                .WithLifetime(ContainerLifetime.Persistent));
        var clustering = storage.AddTables(DigitalBrainNames.Clustering);
        var reminders = storage.AddTables(DigitalBrainNames.Reminders);
        var durableStateStore = storage.AddBlobs(DigitalBrainNames.Journal);
        var grainState = storage.AddBlobs(DigitalBrainNames.GrainState);
        var streams = storage.AddQueues(DigitalBrainNames.Streams);
        var pubSub = storage.AddTables(DigitalBrainNames.PubSub);
        var orleans = builder
            .AddOrleans(name)
            .WithClustering(clustering)
            .WithReminders(reminders)
            .WithGrainStorage(DigitalBrainNames.PubSubStore, pubSub)
            .WithGrainStorage(DigitalBrainNames.DefaultGrainStorage, grainState)
            .WithStreaming(DigitalBrainNames.StreamProvider, streams);
        var brain = new DigitalBrainBuilder(builder, name, orleans, durableStateStore, grainState, streams, pubSub);

        // Silo and clients WaitUntilHealthy for the full fabric before starting.
        brain.RequireHealthyBeforeStart(storage.Resource);
        brain.RequireHealthyBeforeStart(clustering.Resource);
        brain.RequireHealthyBeforeStart(reminders.Resource);
        brain.RequireHealthyBeforeStart(durableStateStore.Resource);
        brain.RequireHealthyBeforeStart(grainState.Resource);
        brain.RequireHealthyBeforeStart(streams.Resource);
        brain.RequireHealthyBeforeStart(pubSub.Resource);
        return brain;
    }

    public static DigitalBrainBuilder AddModule<TModule>(this DigitalBrainBuilder brain)
        where TModule : class
        => brain.AddModule<TModule>(static _ => { });

    public static DigitalBrainBuilder AddModule<TModule>(this DigitalBrainBuilder brain, Action<DigitalBrainModuleBuilder<TModule>> configure)
        where TModule : class
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(configure);
        configure(new DigitalBrainModuleBuilder<TModule>(brain));
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
        builder.WithReference(brain.Streams);
        builder.WithReference(brain.PubSub);

        WaitUntilHealthy(builder, brain.StartupDependencies);

        foreach (var projection in brain.Projections)
        {
            projection.Apply(builder);
        }

        return builder;
    }

    public static IResourceBuilder<TResource> WithReference<TResource>(this IResourceBuilder<TResource> builder, ClientDigitalBrainReference client)
        where TResource : IResourceWithEnvironment, IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(client);

        builder.WithReference(client.Brain.Orleans.AsClient());
        builder.WithReference(client.Brain.Streams);
        // Client processes need clustering tables + streams up before connecting.
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
