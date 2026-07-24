using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Orleans;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Aspire.Hosting;

public sealed class BrainService
{
    private readonly List<Type> _modules = [];
    private readonly List<BrainModuleReference> _moduleReferences = [];
    private IResourceBuilder<AzureBlobStorageResource>? _journal;
    private IResourceBuilder<ParameterResource>? _stateProtectionKey;
    private string? _storageProfile;

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

    internal IResourceBuilder<AzureBlobStorageResource>? Journal => _journal;

    internal IResourceBuilder<ParameterResource>? StateProtectionKey => _stateProtectionKey;

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

    internal void BeginStorageProfile(string profile)
    {
        if (_storageProfile is not null)
        {
            throw new InvalidOperationException(
                $"Brain '{Name}' already uses the '{_storageProfile}' storage profile. Configure storage exactly once.");
        }

        _storageProfile = profile;
    }

    internal void SetJournal(IResourceBuilder<AzureBlobStorageResource> journal) => _journal = journal;

    internal void RequireStateProtection()
    {
        if (_stateProtectionKey is not null)
        {
            return;
        }

        var parameterName = $"{Name}-state-protection-key";
        _stateProtectionKey = Builder.ExecutionContext.IsRunMode
            && _storageProfile is "development"
                ? Builder.AddParameter(
                    parameterName,
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                    secret: true)
                : Builder.AddParameter(parameterName, secret: true);

        _stateProtectionKey.WithDescription(
            "Base64-encoded 256-bit key shared by every silo that recovers encrypted durable module state.");
    }

    public ClientBrainReference AsClient() => new(this);
}

public sealed class ClientBrainReference
{
    internal ClientBrainReference(BrainService brain) => Brain = brain;

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

    public static void RequireStateProtection(BrainService brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.RequireStateProtection();
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

        brain.BeginStorageProfile("development");
        brain.Orleans
            .WithDevelopmentClustering()
            .WithMemoryGrainStorage("journal")
            .WithMemoryReminders();

        return brain;
    }

    public static BrainService WithAzureStorage(
        this BrainService brain,
        IResourceBuilder<AzureStorageResource> storage)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(storage);

        brain.BeginStorageProfile("Azure Storage");
        var clustering = storage.AddTables($"{brain.Name}-clustering");
        var reminders = storage.AddTables($"{brain.Name}-reminders");
        var journal = storage.AddBlobs($"{brain.Name}-journal");

        brain.SetJournal(journal);
        brain.Orleans
            .WithClustering(clustering)
            .WithReminders(reminders);

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

        if (brain.Journal is not null)
        {
            builder
                .WithReference(brain.Journal, "journal")
                .WithAnnotation(new WaitAnnotation(
                    brain.Journal.Resource,
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
            builder.WithEnvironment($"DigitalBrain__Modules__{index}", brain.Modules[index].FullName);
        }

        foreach (var reference in brain.ModuleReferences)
        {
            reference.Apply(builder);
        }

        return builder;
    }

    public static IResourceBuilder<T> WithReference<T>(this IResourceBuilder<T> builder, ClientBrainReference client)
        where T : IResourceWithEnvironment, IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(client);

        return builder.WithReference(client.Brain.Orleans.AsClient());
    }
}
