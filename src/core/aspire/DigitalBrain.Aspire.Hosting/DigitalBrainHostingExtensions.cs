using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Aspire.Hosting;

public static class DigitalBrainHostingExtensions
{
    public static string JournalConnectionName => DigitalBrainResourceNames.JournalConnectionName;

    public static string StateProtectionKeyConfigurationKey
        => DigitalBrainResourceNames.StateProtectionKeyConfigurationKey;

    public static string ModulesConfigurationKey => DigitalBrainResourceNames.ModulesConfigurationKey;

    public static DigitalBrainBuilder AddDigitalBrain(this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var storage = builder
            .AddAzureStorage(DigitalBrainResourceNames.Storage(name))
            .RunAsEmulator();
        var clustering = storage.AddTables(DigitalBrainResourceNames.Clustering(name));
        var reminders = storage.AddTables(DigitalBrainResourceNames.Reminders(name));
        var journal = storage.AddBlobs(DigitalBrainResourceNames.JournalResource(name));
        var streams = storage.AddQueues(DigitalBrainResourceNames.Streams(name));
        var pubSub = storage.AddTables(DigitalBrainResourceNames.PubSub(name));
        var orleans = builder
            .AddOrleans(name)
            .WithClustering(clustering)
            .WithReminders(reminders)
            .WithGrainStorage(DigitalBrainResourceNames.PubSubStoreName, pubSub)
            .WithStreaming(DigitalBrainResourceNames.StreamProviderName, streams);
        var brain = new DigitalBrainBuilder(builder, name, orleans, journal, streams, pubSub);

        brain.RequireHealthyBeforeStart(storage.Resource);
        brain.RequireHealthyBeforeStart(clustering.Resource);
        brain.RequireHealthyBeforeStart(reminders.Resource);
        brain.RequireHealthyBeforeStart(journal.Resource);
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

        brain.UseLocalDevelopmentOAuthCallback(callbackUri.AbsoluteUri);
        return brain;
    }

    public static DigitalBrainBuilder AddModule<TModule>(this DigitalBrainBuilder brain)
        where TModule : class, IModule, new()
        => brain.AddModule<TModule>(static _ => { });

    public static DigitalBrainBuilder AddModule<TModule>(this DigitalBrainBuilder brain, Action<DigitalBrainModuleBuilder<TModule>> configure)
        where TModule : class, IModule, new()
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(configure);
        brain.Select(TModule.Id);
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

        foreach (var dependency in brain.StartupDependencies)
        {
            builder.WithAnnotation(new WaitAnnotation(dependency, WaitType.WaitUntilHealthy, exitCode: 0));
        }

        if (brain.StateProtectionKey is not null)
        {
            builder.WithEnvironment(
                ConfigurationEnvironment(DigitalBrainResourceNames.StateProtectionKeyConfigurationKey),
                brain.StateProtectionKey);
        }

        ProjectModuleManifest(builder, brain);

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
        ProjectModuleManifest(builder, client.Brain);
        return builder;
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

    private static void ProjectModuleManifest<TResource>(IResourceBuilder<TResource> builder, DigitalBrainBuilder brain)
        where TResource : IResourceWithEnvironment
        => builder.WithEnvironment(context =>
        {
            for (var index = 0; index < brain.Modules.Count; index++)
            {
                context.EnvironmentVariables[
                    $"{ConfigurationEnvironment(DigitalBrainResourceNames.ModulesConfigurationKey)}__{index}"] =
                    brain.Modules[index].Value;
            }
        });

    private static string ConfigurationEnvironment(string configurationKey)
        => configurationKey.Replace(":", "__", StringComparison.Ordinal);
}
