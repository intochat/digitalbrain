using System.ComponentModel;
using System.Runtime.CompilerServices;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Orleans;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Aspire.Hosting;

public sealed class BrainService
{
    private readonly List<Type> _modules = [];
    private readonly List<BrainModuleReference> _moduleReferences = [];

    internal BrainService(IDistributedApplicationBuilder builder, string name)
    {
        Builder = builder;
        Name = name;
        Orleans = builder.AddOrleans(name);
    }

    public IDistributedApplicationBuilder Builder { get; }

    public string Name { get; }

    internal OrleansService Orleans { get; }

    internal IReadOnlyList<Type> Modules => _modules;

    internal IReadOnlyList<BrainModuleReference> ModuleReferences => _moduleReferences;

    internal bool TryActivate(Type module)
    {
        if (_modules.Contains(module))
        {
            return false;
        }

        _modules.Add(module);

        return true;
    }

    internal void Deactivate(Type module) => _modules.Remove(module);

    internal void AddReference(BrainModuleReference reference) => _moduleReferences.Add(reference);

    public BrainClientService AsClient() => new(this);
}

public sealed class BrainClientService
{
    internal BrainClientService(BrainService brain) => Brain = brain;

    internal BrainService Brain { get; }
}

[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class BrainModuleReference
{
    public abstract void Apply<T>(IResourceBuilder<T> builder)
        where T : IResourceWithEnvironment, IResourceWithEndpoints;
}

[EditorBrowsable(EditorBrowsableState.Never)]
public static class BrainModuleHosting
{
    private static readonly ConditionalWeakTable<IModule, BrainService> Brains = new();

    public static BrainService BrainOf(IModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        return Brains.TryGetValue(module, out var brain)
            ? brain
            : throw new InvalidOperationException(
                $"{module.GetType().Name} can be configured only inside brain.AddModule<{module.GetType().Name}>(...).");
    }

    public static void AddReference(BrainService brain, BrainModuleReference reference)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(reference);

        brain.AddReference(reference);
    }

    internal static void Bind(IModule module, BrainService brain) => Brains.Add(module, brain);

    internal static void Unbind(IModule module) => Brains.Remove(module);
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

    public static BrainService AddModule<TModule>(
        this BrainService brain,
        Action<TModule> configure)
        where TModule : class, IModule, new()
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(configure);

        if (!brain.TryActivate(typeof(TModule)))
        {
            throw new InvalidOperationException(
                $"{typeof(TModule).Name} is already configured on brain '{brain.Name}'. Add each module exactly once.");
        }

        var module = new TModule();
        BrainModuleHosting.Bind(module, brain);

        try
        {
            configure(module);
        }
        catch
        {
            brain.Deactivate(typeof(TModule));
            throw;
        }
        finally
        {
            BrainModuleHosting.Unbind(module);
        }

        return brain;
    }

    public static IResourceBuilder<T> WithReference<T>(this IResourceBuilder<T> builder, BrainService brain)
        where T : IResourceWithEnvironment, IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(brain);

        builder.WithReference(brain.Orleans);

        for (var index = 0; index < brain.Modules.Count; index++)
        {
            builder.WithEnvironment($"DigitalBrain__Modules__{index}", brain.Modules[index].FullName);
        }

        foreach (var reference in brain.ModuleReferences)
        {
            reference.Apply(builder);
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
