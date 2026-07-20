using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Orleans;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Aspire.Hosting;

public sealed class BrainService
{
    internal BrainService(IDistributedApplicationBuilder builder, string name)
    {
        Builder = builder;
        Name = name;
        Orleans = builder.AddOrleans(name);
    }

    public IDistributedApplicationBuilder Builder { get; }

    public string Name { get; }

    internal OrleansService Orleans { get; }

    public BrainClientService AsClient() => new(this);
}

public sealed class BrainClientService
{
    internal BrainClientService(BrainService brain) => Brain = brain;

    internal BrainService Brain { get; }
}

public static class BrainHostingExtensions
{
    public static BrainService AddBrain(this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new BrainService(builder, name);
    }

    public static BrainService WithDevelopmentStores(this BrainService brain)
    {
        ArgumentNullException.ThrowIfNull(brain);

        brain.Orleans
            .WithDevelopmentClustering()
            .WithMemoryGrainStorage("journal")
            .WithMemoryReminders();

        return brain;
    }

    public static IResourceBuilder<T> WithReference<T>(this IResourceBuilder<T> builder, BrainService brain)
        where T : IResourceWithEnvironment, IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(brain);

        builder.WithReference(brain.Orleans);

        return builder;
    }

    public static IResourceBuilder<T> WithReference<T>(this IResourceBuilder<T> builder, BrainClientService client)
        where T : IResourceWithEnvironment, IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(client);

        return builder.WithReference(client.Brain.Orleans.AsClient());
    }
}
