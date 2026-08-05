using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Journaling;
using Orleans.Journaling.Json;
using Orleans.Serialization;

namespace DigitalBrain;

public static class DigitalBrainSiloExtensions
{
    public static ISiloBuilder AddDigitalBrain(this ISiloBuilder silo, params Assembly[] moduleAssemblies)
    {
        ArgumentNullException.ThrowIfNull(moduleAssemblies);
        return silo.AddDigitalBrain(ModuleTypesOf(moduleAssemblies));
    }

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
        silo.Services.AddScoped<NeuronJournal>();

        silo.Services.AddSingleton(new CatalogFingerprint(catalog.Fingerprint));
        silo.Services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>, CatalogFingerprintAnnouncement>();

        silo.AddIncomingGrainCallFilter<IncomingSynapseFilter>();
        silo.AddOutgoingGrainCallFilter<OutgoingSynapseFilter>();

        silo.AddJournalStorage();
        GateDurableKeys(silo.Services);
        silo.Services.TryAddSingleton<IJournalStorageProvider, VolatileJournalStorageProvider>();
        silo.UseJsonJournalFormat(JournalJsonContext.Default);

        silo.Services.AddSerializer(wire => ConfigureWire(wire, neuronTypes, codec));

        return silo;
    }

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

        // Nested Core grain interfaces travel by bare CLR name; vouch them via type filter.
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

        neuronTypes.Add(typeof(Neuron.Session));
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

    // Open-generic IDurable* cannot be keyed-factory-wrapped; gate registration at the state manager.
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
