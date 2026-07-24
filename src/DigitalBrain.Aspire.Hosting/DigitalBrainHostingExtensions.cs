using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Orleans;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Aspire.Hosting;

public static class DigitalBrainHostingExtensions
{
    public static DigitalBrainBuilder AddDigitalBrain(
        this IDistributedApplicationBuilder builder,
        string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var orleans = builder
            .AddOrleans(name)
            .WithDevelopmentClustering()
            .WithMemoryGrainStorage("journal")
            .WithMemoryReminders();

        return new DigitalBrainBuilder(builder, name, orleans);
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
            builder.WithReference(brain.Journal, "journal");
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
                "DigitalBrain__Security__StateProtectionKey",
                brain.StateProtectionKey);
        }

        for (var index = 0; index < brain.Modules.Count; index++)
        {
            builder.WithEnvironment(
                $"DigitalBrain__Modules__{index}",
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
}
