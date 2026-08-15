using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire.Hosting;

public static class DigitalBrainHostingExtensions
{
    public static string JournalConnectionName => DigitalBrainNames.JournalConnection;

    public static string StateProtectionKeyConfigurationKey => DigitalBrainNames.StateProtectionKey;

    public static DigitalBrainBuilder AddDigitalBrain(this IDistributedApplicationBuilder builder, string name)
    {
        var storage = builder
            .AddAzureStorage(DigitalBrainNames.Storage)
            .RunAsEmulator(emulator => emulator
                .WithDataVolume()
                .WithLifetime(ContainerLifetime.Persistent));

        var clustering = storage.AddTables(DigitalBrainNames.Clustering);
        var reminders = storage.AddTables(DigitalBrainNames.Reminders);
        var journal = storage.AddBlobs(DigitalBrainNames.Journal);
        var streams = storage.AddQueues(DigitalBrainNames.Streams);
        var pubSub = storage.AddTables(DigitalBrainNames.PubSub);

        var orleans = builder
            .AddOrleans(name)
            .WithClustering(clustering)
            .WithReminders(reminders)
            .WithGrainStorage(DigitalBrainNames.PubSubStore, pubSub)
            .WithStreaming(DigitalBrainNames.StreamProvider, streams);

        var brain = new DigitalBrainBuilder(builder, name, orleans, journal, streams, pubSub);

        brain.RequireHealthyBeforeStart(storage.Resource);
        brain.RequireHealthyBeforeStart(clustering.Resource);
        brain.RequireHealthyBeforeStart(reminders.Resource);
        brain.RequireHealthyBeforeStart(journal.Resource);
        brain.RequireHealthyBeforeStart(streams.Resource);
        brain.RequireHealthyBeforeStart(pubSub.Resource);
        return brain;
    }

    public static DigitalBrainBuilder WithOwner(this DigitalBrainBuilder brain, string owner)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        brain.UseOwner(owner);
        return brain;
    }

    public static DigitalBrainBuilder WithLocalDevelopmentOAuthCallback(
        this DigitalBrainBuilder brain,
        Uri callbackUri)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(callbackUri);
        if (!callbackUri.IsAbsoluteUri)
        {
            throw new ArgumentException(
                "The local-development OAuth callback must be an absolute URI.",
                nameof(callbackUri));
        }

        if (!OAuthCallbackPaths.EndsWithCanonicalCallback(callbackUri))
        {
            throw new ArgumentException(
                $"The local-development OAuth callback must end with '{OAuthCallbackPaths.RelativePath}' "
                + $"(the path the kernel serves). Received '{callbackUri}'.",
                nameof(callbackUri));
        }

        brain.UseLocalDevelopmentOAuthCallback(callbackUri.AbsoluteUri);
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
        builder.WithReference(brain.Journal, DigitalBrainNames.JournalConnection);
        builder.WithReference(brain.Streams);
        builder.WithReference(brain.PubSub);

        ApplyOwner(builder, brain);
        WaitUntilHealthy(builder, brain.StartupDependencies);

        if (brain.StateProtectionKey is not null)
        {
            builder.WithEnvironment(
                ConfigurationEnvironment(DigitalBrainNames.StateProtectionKey),
                brain.StateProtectionKey);
        }

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
        ApplyOwner(builder, client.Brain);
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

    public static IResourceBuilder<TResource> WithStateProtectionKey<TResource>(
        this IResourceBuilder<TResource> builder,
        DigitalBrainBuilder brain)
        where TResource : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(brain);

        brain.RequireStateProtection();
        if (brain.StateProtectionKey is not null)
        {
            builder.WithEnvironment(
                ConfigurationEnvironment(DigitalBrainNames.StateProtectionKey),
                brain.StateProtectionKey);
        }

        return builder;
    }

    private static void ApplyOwner<TResource>(IResourceBuilder<TResource> builder, DigitalBrainBuilder brain)
        where TResource : IResourceWithEnvironment
    {
        builder.WithEnvironment(
            ConfigurationEnvironment(DigitalBrainNames.Owner),
            brain.Owner);
    }

    private static string ConfigurationEnvironment(string configurationKey)
        => configurationKey.Replace(":", "__", StringComparison.Ordinal);
}
