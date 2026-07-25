using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Orleans;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Aspire.Hosting;

public static class DigitalBrainHostingExtensions
{
    public const string JournalConnectionName = "journal";
    public const string StateProtectionKeyConfigurationKey = "DigitalBrain:Security:StateProtectionKey";
    public const string ModulesConfigurationKey = "DigitalBrain:Modules";

    public static DigitalBrainBuilder AddDigitalBrain(
        this IDistributedApplicationBuilder builder,
        string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var storage = builder
            .AddAzureStorage($"{name}-storage")
            .RunAsEmulator();
        var clustering = storage.AddTables($"{name}-clustering");
        var reminders = storage.AddTables($"{name}-reminders");
        var journal = storage.AddBlobs($"{name}-journal");
        var orleans = builder
            .AddOrleans(name)
            .WithClustering(clustering)
            .WithReminders(reminders);
        var brain = new DigitalBrainBuilder(builder, name, orleans);

        brain.SetJournal(journal);
        brain.RequireHealthyBeforeStart(storage.Resource);
        brain.RequireHealthyBeforeStart(clustering.Resource);
        brain.RequireHealthyBeforeStart(reminders.Resource);
        brain.RequireHealthyBeforeStart(journal.Resource);
        return brain;
    }

    public static DigitalBrainBuilder AddModule<TModule>(this DigitalBrainBuilder brain)
        where TModule : class, IModule, new()
        => brain.AddModule<TModule>(static _ => { });

    public static DigitalBrainBuilder AddModule<TModule>(
        this DigitalBrainBuilder brain,
        Action<DigitalBrainModuleBuilder<TModule>> configure)
        where TModule : class, IModule, new()
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(configure);
        brain.Select(TModule.Id);
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

        if (brain.Journal is not null)
        {
            builder.WithReference(brain.Journal, JournalConnectionName);
        }

        foreach (var dependency in brain.StartupDependencies)
        {
            builder.WithAnnotation(new WaitAnnotation(
                dependency,
                WaitType.WaitUntilHealthy,
                exitCode: 0));
        }

        if (brain.StateProtectionKey is not null)
        {
            builder.WithEnvironment(
                ConfigurationEnvironment(StateProtectionKeyConfigurationKey),
                brain.StateProtectionKey);
        }

        for (var index = 0; index < brain.Modules.Count; index++)
        {
            builder.WithEnvironment(
                $"{ConfigurationEnvironment(ModulesConfigurationKey)}__{index}",
                brain.Modules[index].Value);
        }

        foreach (var projection in brain.Projections)
        {
            projection.Apply(builder);
        }

        return builder;
    }

    public static IResourceBuilder<TResource> WithReference<TResource>(
        this IResourceBuilder<TResource> builder,
        ClientDigitalBrainReference client)
        where TResource : IResourceWithEnvironment, IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(client);

        return builder.WithReference(client.Brain.Orleans.AsClient());
    }

    private static string ConfigurationEnvironment(string configurationKey)
        => configurationKey.Replace(":", "__", StringComparison.Ordinal);
}
