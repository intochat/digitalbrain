using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Aspire.Hosting;

public static class DigitalBrainHostingExtensions
{
    public static string JournalConnectionName => DigitalBrainResourceNames.JournalConnectionName;

    public static string StateProtectionKeyConfigurationKey
        => DigitalBrainResourceNames.StateProtectionKeyConfigurationKey;

    public static DigitalBrainBuilder AddDigitalBrain(this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var storage = builder
            .AddAzureStorage(DigitalBrainResourceNames.Storage)
            .RunAsEmulator(static emulator => emulator
                .WithDataVolume()
                .WithLifetime(ContainerLifetime.Persistent));
        var clustering = storage.AddTables(DigitalBrainResourceNames.Clustering);
        var reminders = storage.AddTables(DigitalBrainResourceNames.Reminders);
        var journal = storage.AddBlobs(DigitalBrainResourceNames.JournalResource);
        var streams = storage.AddQueues(DigitalBrainResourceNames.Streams);
        var pubSub = storage.AddTables(DigitalBrainResourceNames.PubSub);
        var orleans = builder
            .AddOrleans(name)
            .WithClustering(clustering)
            .WithReminders(reminders)
            .WithGrainStorage(DigitalBrainResourceNames.PubSubStoreName, pubSub)
            .WithStreaming(DigitalBrainResourceNames.StreamProviderName, streams);
        var brain = new DigitalBrainBuilder(builder, name, orleans, journal, streams, pubSub);

        // Silo and clients WaitUntilHealthy for the full fabric before starting.
        brain.RequireHealthyBeforeStart(storage.Resource);
        brain.RequireHealthyBeforeStart(clustering.Resource);
        brain.RequireHealthyBeforeStart(reminders.Resource);
        brain.RequireHealthyBeforeStart(journal.Resource);
        brain.RequireHealthyBeforeStart(streams.Resource);
        brain.RequireHealthyBeforeStart(pubSub.Resource);
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
        builder.WithReference(brain.Journal, DigitalBrainResourceNames.JournalConnectionName);
        builder.WithReference(brain.Streams);
        builder.WithReference(brain.PubSub);

        WaitUntilHealthy(builder, brain.StartupDependencies);

        if (brain.StateProtectionKey is not null)
        {
            builder.WithEnvironment(
                ConfigurationEnvironment(DigitalBrainResourceNames.StateProtectionKeyConfigurationKey),
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
                ConfigurationEnvironment(DigitalBrainResourceNames.StateProtectionKeyConfigurationKey),
                brain.StateProtectionKey);
        }

        return builder;
    }

    private static string ConfigurationEnvironment(string configurationKey)
        => configurationKey.Replace(":", "__", StringComparison.Ordinal);
}
