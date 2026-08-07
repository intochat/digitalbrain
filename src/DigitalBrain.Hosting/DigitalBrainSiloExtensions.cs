using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Journaling;
using Orleans.Journaling.Json;
using Orleans.Serialization;

namespace DigitalBrain;

public static class DigitalBrainSiloExtensions
{
    public static ISiloBuilder AddDigitalBrain(
        this ISiloBuilder silo,
        Action<DigitalBrainComposition> configure,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(silo);
        var catalog = BuildCatalog(configure);
        var serialization = ValidateAndCreateSerialization(catalog);

        silo.Services.AddSingleton(catalog);
        silo.Services.AddSingleton<ISynapseSerialization>(serialization);
        silo.Services.AddSingleton<Router>();
        silo.Services.AddSingleton<IEnvelopeCarrier, RequestContextEnvelopeCarrier>();
        silo.Services.AddSingleton(new DigitalBrainClock(timeProvider ?? TimeProvider.System));
        silo.Services.AddScoped<Journal>(static provider => new Journal(provider));
        AddWorkspaceServices(silo.Services, catalog);
        silo.AddJournalStorage();
        GateDurableKeys(silo.Services);
        silo.Services.TryAddSingleton<IJournalStorageProvider, VolatileJournalStorageProvider>();
        silo.UseJsonJournalFormat(JournalJsonContext.Default);
        silo.Services.AddSerializer(wire => ConfigureWire(wire, catalog, serialization));
        return silo;
    }

    public static IServiceCollection AddDigitalBrainSerialization(
        this IServiceCollection services,
        Action<DigitalBrainComposition> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        var catalog = BuildCatalog(configure);
        var serialization = ValidateAndCreateSerialization(catalog);

        services.AddSingleton(catalog);
        services.AddSingleton<ISynapseSerialization>(serialization);
        services.AddSerializer(wire => ConfigureWire(wire, catalog, serialization));
        return services;
    }

    private static CompositionCatalog BuildCatalog(Action<DigitalBrainComposition> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var composition = new DigitalBrainComposition();
        configure(composition);
        return composition.Seal();
    }

    private static SynapseSerialization ValidateAndCreateSerialization(CompositionCatalog catalog)
    {
        var serialization = new SynapseSerialization(catalog);
        serialization.ValidateVocabulary();
        foreach (var behavior in catalog.BehaviorTypes)
        {
            if (CompositionCatalog.StateTypeOf(behavior) is { } state)
            {
                serialization.ValidateState(state);
            }
        }

        return serialization;
    }

    private static void AddWorkspaceServices(IServiceCollection services, CompositionCatalog catalog)
    {
        services.AddScoped<WorkspaceBindingHolder>();
        foreach (var registration in catalog.WorkspaceServices)
        {
            services.AddScoped(
                registration.ServiceType,
                provider => registration.Factory(
                    provider.GetRequiredService<WorkspaceBindingHolder>().Binding));
        }
    }

    private static void ConfigureWire(
        ISerializerBuilder wire,
        CompositionCatalog catalog,
        SynapseSerialization serialization)
    {
        foreach (var assembly in catalog.WireAssemblies)
        {
            wire.AddAssembly(assembly);
        }

        wire.Services.AddSingleton<ITypeFilter, HostingWireTypeFilter>();
        wire.AddJsonSerializer(IsWireContract, serialization.Options);
    }

    private static bool IsWireContract(Type type)
        => typeof(Synapse).IsAssignableFrom(type)
            || typeof(JournalRead).IsAssignableFrom(type);

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
