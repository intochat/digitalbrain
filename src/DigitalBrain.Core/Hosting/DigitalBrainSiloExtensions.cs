using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Orleans.Journaling;
using Orleans.Journaling.Json;
using Orleans.Serialization;

namespace DigitalBrain;

// The hosting seam (§8): one call turns a silo into a brain. Everything the boot must
// refuse — kind collisions, dead answerer claims, codec-unresolvable vocabulary, TState
// contract breaches — fires HERE, before the silo ever forms. Catalog and codec are
// per-silo DI instances, never static: test clusters compose independently.
public static class DigitalBrainSiloExtensions
{
    public static ISiloBuilder AddDigitalBrain(this ISiloBuilder silo, params Assembly[] moduleAssemblies)
    {
        ArgumentNullException.ThrowIfNull(moduleAssemblies);
        return silo.AddDigitalBrain(ModuleTypesOf(moduleAssemblies));
    }

    // The explicit-type composition seam (§3: the catalog is built over "the composition's
    // explicit neuron type set"): the assembly overload is host convenience over this one.
    // DigitalBrain.Testing composes per-test-fixture module sets through it, so two
    // compositions in one test assembly never bleed listeners into each other's catalogs.
    public static ISiloBuilder AddDigitalBrain(this ISiloBuilder silo, IEnumerable<Type> moduleTypes)
    {
        ArgumentNullException.ThrowIfNull(silo);
        ArgumentNullException.ThrowIfNull(moduleTypes);

        var neuronTypes = NeuronTypesOf(moduleTypes);
        var catalog = Catalog.Build(neuronTypes);
        var codec = new BodyCodec(catalog);
        codec.ValidateVocabulary(catalog);
        foreach (var neuronType in neuronTypes)
        {
            if (StateTypeOf(neuronType) is { } stateType)
            {
                codec.ValidateState(stateType);
            }
        }

        silo.Services.AddSingleton(catalog);
        silo.Services.AddSingleton(codec);
        silo.Services.AddScoped<NeuronJournal>();   // one per activation, beside its IDurable*

        // The fingerprint is computed and registered now; logging announces it at silo
        // start. The cluster-join refusal for a mismatched silo needs cluster machinery
        // Core does not yet own — deferred, stated, never inherited silently.
        silo.Services.AddSingleton(new CatalogFingerprint(catalog.Fingerprint));
        silo.Services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>, CatalogFingerprintAnnouncement>();

        silo.AddIncomingGrainCallFilter<IncomingSynapseFilter>();
        silo.AddOutgoingGrainCallFilter<OutgoingSynapseFilter>();

        silo.AddJournalStorage();
        GateDurableKeys(silo.Services);
        silo.Services.TryAddSingleton<IJournalStorageProvider, VolatileJournalStorageProvider>();
        silo.UseJsonJournalFormat(JournalJsonContext.Default);

        // The wire codec: module fact records, the envelope shapes and the public read
        // shapes travel as JSON through the body codec's own options — journal = wire.
        // Core's and the modules' assemblies register explicitly: the transport interface
        // types must be in every host's type manifest, and ambient entry-assembly
        // discovery is not a contract (proven live: a TestingHost silo refused
        // Neuron+ISessionEntry as "not allowed" without this).
        silo.Services.AddSerializer(wire => ConfigureWire(wire, neuronTypes, codec));

        return silo;
    }

    // The client half of the wire codec: a cluster client that reads journals or fires
    // session calls needs the same JSON codec over the same vocabulary.
    public static IServiceCollection AddDigitalBrainWireCodec(
        this IServiceCollection services, params Assembly[] moduleAssemblies)
    {
        ArgumentNullException.ThrowIfNull(moduleAssemblies);
        return services.AddDigitalBrainWireCodec(ModuleTypesOf(moduleAssemblies));
    }

    public static IServiceCollection AddDigitalBrainWireCodec(
        this IServiceCollection services, IEnumerable<Type> moduleTypes)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(moduleTypes);

        var neuronTypes = NeuronTypesOf(moduleTypes);
        var codec = new BodyCodec(Catalog.Build(neuronTypes));
        return services.AddSerializer(wire => ConfigureWire(wire, neuronTypes, codec));
    }

    private static void ConfigureWire(ISerializerBuilder wire, IReadOnlyList<Type> neuronTypes, BodyCodec codec)
    {
        foreach (var assembly in neuronTypes.Select(neuronType => neuronType.Assembly).Distinct())
        {
            wire.AddAssembly(assembly);
        }

        // Compound type aliases transmit Core's grain interfaces by bare CLR name (no
        // assembly); the manifest-derived allowed-name set holds assembly-qualified keys
        // and treats the nested name's components as unknown, so the resolved type must
        // be vouched for by a type filter (proven live: "Type
        // 'DigitalBrain.Neuron+ISessionEntry' is not allowed"). Core vouches for exactly
        // its own wire interfaces — typed, no name-parsing semantics to drift.
        wire.Services.AddSingleton<ITypeFilter, CoreWireTypeFilter>();

        wire.AddJsonSerializer(IsWireContract, codec.Options);
    }

    private static Type[] ModuleTypesOf(Assembly[] moduleAssemblies)
        => [.. moduleAssemblies
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(Neuron).IsAssignableFrom(type) && !type.IsAbstract)];

    private static Type[] NeuronTypesOf(IEnumerable<Type> moduleTypes)
    {
        var neuronTypes = new List<Type>();
        foreach (var moduleType in moduleTypes.Distinct())
        {
            if (!typeof(Neuron).IsAssignableFrom(moduleType) || moduleType.IsAbstract)
            {
                throw new InvalidOperationException(
                    $"{Catalog.Describe(moduleType)} is not a concrete Neuron; a composition lists module "
                    + "neuron classes only.");
            }

            neuronTypes.Add(moduleType);
        }

        neuronTypes.Add(typeof(Neuron.Session));   // the Core-owned "session" kind, reserved by presence
        return [.. neuronTypes];
    }

    private static Type? StateTypeOf(Type neuronType)
    {
        for (var ancestor = neuronType.BaseType; ancestor is not null; ancestor = ancestor.BaseType)
        {
            if (ancestor.IsGenericType && ancestor.GetGenericTypeDefinition() == typeof(Neuron<>))
            {
                return ancestor.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static bool IsWireContract(Type type)
        => typeof(Synapse).IsAssignableFrom(type)
            || type == typeof(SynapseMetadata)
            || type == typeof(NeuronId)
            || type == typeof(SynapseRef)
            || type == typeof(NeuronReading)
            || type == typeof(JournalFact)
            || type == typeof(Delivery);

    // The DI gatekeeper (§5): Orleans.Journaling registers IDurable* as open-generic keyed
    // scoped services under AnyKey — un-wrappable per key (open generics take no factory) —
    // but every durable structure announces itself to the activation's state manager, so
    // the gate wraps THAT: a module-minted key fails activation loudly naming the rule.
    private static void GateDurableKeys(IServiceCollection services)
    {
        var stateManager = services.LastOrDefault(descriptor
                => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(IJournaledStateManager))
            ?? throw new InvalidOperationException(
                "Orleans.Journaling registered no IJournaledStateManager; the durable-key "
                + "gatekeeper has nothing to wrap — the journaling package surface changed.");

        services.Remove(stateManager);
        services.Add(ServiceDescriptor.Describe(
            typeof(IJournaledStateManager),
            provider => new GatedStateManager(InstantiateInner(provider, stateManager)),
            stateManager.Lifetime));
    }

    private static IJournaledStateManager InstantiateInner(IServiceProvider provider, ServiceDescriptor descriptor)
        => descriptor switch
        {
            { ImplementationInstance: IJournaledStateManager instance } => instance,
            { ImplementationFactory: { } factory } => (IJournaledStateManager)factory(provider),
            { ImplementationType: { } implementation }
                => (IJournaledStateManager)ActivatorUtilities.CreateInstance(provider, implementation),
            _ => throw new InvalidOperationException(
                "The IJournaledStateManager registration carries no implementation to wrap."),
        };
}

// The wire's own passport: exactly the Core grain interfaces, nothing else — everything a
// module says travels as Synapse through the JSON wire codec, which vouches for itself.
internal sealed class CoreWireTypeFilter : ITypeFilter
{
    private static readonly HashSet<Type> CoreWireTypes =
    [
        typeof(Neuron.ITransport), typeof(Neuron.IDrainEntry),
        typeof(Neuron.ISessionEntry), typeof(IOutboxWakeup),
    ];

    public bool? IsTypeAllowed(Type type) => CoreWireTypes.Contains(type) ? true : null;
}

// The running composition's identity: a hash of the sorted declaration rows. Silos whose
// fingerprints differ must refuse to form one brain (§3) — the enforcement lands with
// cluster machinery; the identity itself is minted and visible from day one.
public sealed record CatalogFingerprint(string Value);

internal sealed partial class CatalogFingerprintAnnouncement(
    ILogger<CatalogFingerprintAnnouncement> logger,
    CatalogFingerprint fingerprint) : ILifecycleParticipant<ISiloLifecycle>
{
    public void Participate(ISiloLifecycle observer)
        => observer.Subscribe(
            nameof(CatalogFingerprintAnnouncement),
            ServiceLifecycleStage.ApplicationServices,
            _ =>
            {
                LogCatalogFingerprint(logger, fingerprint.Value);
                return Task.CompletedTask;
            });

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "DigitalBrain catalog fingerprint {CatalogFingerprint}; every silo of one brain must match")]
    private static partial void LogCatalogFingerprint(ILogger logger, string catalogFingerprint);
}

// Wraps the per-activation state manager so every durable structure's self-registration
// passes the Core-owned key gate; everything else delegates, including grain-lifecycle
// participation — the wrapper must be invisible to the load/commit path it guards.
internal sealed class GatedStateManager(IJournaledStateManager inner)
    : IJournaledStateManager, ILifecycleParticipant<IGrainLifecycle>
{
    public long PendingWriteByteCount => inner.PendingWriteByteCount;

    public ValueTask InitializeAsync(CancellationToken cancellationToken)
        => inner.InitializeAsync(cancellationToken);

    public void RegisterState(string name, IJournaledState state)
    {
        if (!NeuronJournal.CoreKeys.Contains(name))
        {
            throw new InvalidOperationException(
                $"Durable key '{name}' is not Core-owned; keyed IDurable* resolution is sealed away "
                + "from modules — all durable module state lives in TState (§5).");
        }

        inner.RegisterState(name, state);
    }

    public bool TryGetState(string name, [NotNullWhen(true)] out IJournaledState? state)
        => inner.TryGetState(name, out state);

    public ValueTask WriteStateAsync(CancellationToken cancellationToken)
        => inner.WriteStateAsync(cancellationToken);

    public ValueTask DeleteStateAsync(CancellationToken cancellationToken)
        => inner.DeleteStateAsync(cancellationToken);

    public ValueTask DisposeAsync() => inner.DisposeAsync();

    public void Participate(IGrainLifecycle observer)
    {
        if (inner is ILifecycleParticipant<IGrainLifecycle> participant)
        {
            participant.Participate(observer);
        }
    }
}
