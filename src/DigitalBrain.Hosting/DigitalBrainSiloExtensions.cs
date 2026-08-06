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
        var neurons = NeuronTypesOf(moduleTypes);
        var catalog = Catalog.Build(neurons);
        var codec = new BodyCodec(catalog);
        codec.ValidateVocabulary(catalog);
        foreach (var neuron in neurons)
        {
            if (StateTypeOf(neuron) is { } state)
            {
                codec.ValidateState(state);
            }
        }

        silo.Services.AddSingleton<ICatalog>(catalog);
        silo.Services.AddSingleton<ISynapseCodec>(codec);
        silo.Services.AddSingleton<IEnvelopeCarrier, RequestContextEnvelopeCarrier>();
        silo.Services.AddScoped<Journal>(provider => new Journal(provider));
        silo.AddJournalStorage();
        GateDurableKeys(silo.Services);
        silo.Services.TryAddSingleton<IJournalStorageProvider, VolatileJournalStorageProvider>();
        silo.UseJsonJournalFormat(JournalJsonContext.Default);
        silo.Services.AddSerializer(wire => ConfigureWire(wire, neurons, codec));
        return silo;
    }

    public static IServiceCollection AddDigitalBrainWireCodec(
        this IServiceCollection services,
        params Assembly[] moduleAssemblies)
    {
        ArgumentNullException.ThrowIfNull(moduleAssemblies);
        return services.AddDigitalBrainWireCodec(ModuleTypesOf(moduleAssemblies));
    }

    public static IServiceCollection AddDigitalBrainWireCodec(
        this IServiceCollection services,
        IEnumerable<Type> moduleTypes)
    {
        ArgumentNullException.ThrowIfNull(services);
        var neurons = NeuronTypesOf(moduleTypes);
        var codec = new BodyCodec(Catalog.Build(neurons));
        return services.AddSerializer(wire => ConfigureWire(wire, neurons, codec));
    }

    private static void ConfigureWire(ISerializerBuilder wire, IReadOnlyList<Type> neurons, BodyCodec codec)
    {
        foreach (var assembly in neurons.Select(neuron => neuron.Assembly)
                     .Append(typeof(Ingress).Assembly)
                     .Distinct())
        {
            wire.AddAssembly(assembly);
        }

        wire.Services.AddSingleton<ITypeFilter, CoreWireTypeFilter>();
        wire.AddJsonSerializer(IsWireContract, codec.Options);
    }

    private static Type[] ModuleTypesOf(IEnumerable<Assembly> assemblies)
        => [.. assemblies
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(Neuron).IsAssignableFrom(type) && !type.IsAbstract)];

    private static Type[] NeuronTypesOf(IEnumerable<Type> moduleTypes)
    {
        ArgumentNullException.ThrowIfNull(moduleTypes);
        var neurons = moduleTypes.Distinct().ToArray();
        foreach (var type in neurons)
        {
            if (!typeof(Neuron).IsAssignableFrom(type) || type.IsAbstract)
            {
                throw new InvalidOperationException($"{Catalog.Describe(type)} is not a concrete neuron.");
            }
        }

        return neurons;
    }

    private static Type? StateTypeOf(Type neuron)
    {
        for (var current = neuron.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Neuron<>))
            {
                return current.GetGenericArguments()[0];
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
            || type == typeof(JournalFact);

    private static void GateDurableKeys(IServiceCollection services)
    {
        var stateManager = services.LastOrDefault(descriptor
                => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(IJournaledStateManager))
            ?? throw new InvalidOperationException("Orleans.Journaling registered no state manager.");
        services.Remove(stateManager);
        services.Add(ServiceDescriptor.Describe(
            typeof(IJournaledStateManager),
            provider => new GatedStateManager(Instantiate(provider, stateManager)),
            stateManager.Lifetime));
    }

    private static IJournaledStateManager Instantiate(IServiceProvider provider, ServiceDescriptor descriptor)
        => descriptor switch
        {
            { ImplementationInstance: IJournaledStateManager instance } => instance,
            { ImplementationFactory: { } factory } => (IJournaledStateManager)factory(provider),
            { ImplementationType: { } implementation } => (IJournaledStateManager)ActivatorUtilities.CreateInstance(provider, implementation),
            _ => throw new InvalidOperationException("The journal state manager registration has no implementation."),
        };
}
