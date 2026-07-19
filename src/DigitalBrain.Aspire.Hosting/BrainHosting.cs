using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Orleans;
using DigitalBrain.Abstractions;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Aspire.Hosting;

public sealed class BrainService
{
    private readonly List<DeclaredModel> _models = [];

    internal BrainService(IDistributedApplicationBuilder builder, string name)
    {
        Builder = builder;
        Name = name;
        Orleans = builder.AddOrleans(name);
    }

    public IDistributedApplicationBuilder Builder { get; }

    public string Name { get; }

    internal OrleansService Orleans { get; }

    internal IReadOnlyList<DeclaredModel> Models => _models;

    public BrainService WithModel(
        ModelTier tier,
        string provider,
        string modelId,
        IResourceBuilder<ParameterResource> apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(apiKey);

        if (_models.Any(declared => declared.Tier == tier))
        {
            throw new InvalidOperationException($"The {tier} tier is already bound on brain '{Name}'. Each tier binds to exactly one model.");
        }

        _models.Add(new DeclaredModel(tier, provider, modelId, apiKey));

        return this;
    }

    public BrainClientService AsClient() => new(this);

    internal sealed record DeclaredModel(
        ModelTier Tier,
        string Provider,
        string ModelId,
        IResourceBuilder<ParameterResource> ApiKey);
}

public sealed class BrainClientService
{
    internal BrainClientService(BrainService brain) => Brain = brain;

    internal BrainService Brain { get; }
}

public static class BrainHostingExtensions
{
    public const string ModelConfigurationPrefix = "DigitalBrain__Models";

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

        for (var index = 0; index < brain.Models.Count; index++)
        {
            var model = brain.Models[index];

            builder
                .WithEnvironment($"{ModelConfigurationPrefix}__{index}__Tier", model.Tier.ToString())
                .WithEnvironment($"{ModelConfigurationPrefix}__{index}__Provider", model.Provider)
                .WithEnvironment($"{ModelConfigurationPrefix}__{index}__ModelId", model.ModelId)
                .WithEnvironment($"{ModelConfigurationPrefix}__{index}__ApiKey", model.ApiKey);
        }

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
