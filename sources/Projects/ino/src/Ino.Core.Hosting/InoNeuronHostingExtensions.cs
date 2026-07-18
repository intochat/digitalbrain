using Core.AI;
using Ino.Core.Capabilities;
using Ino.Core.Hosting.Capabilities;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace Ino.Core.Hosting;

public static class InoNeuronHostingExtensions
{
    // Registers InoNeuron's dependencies inside a UseOrleans callback so
    // Orleans picks up the [AgentState] facet mapper at grain-activation time.
    // Must be called on ISiloBuilder (inside UseOrleans), NOT on builder.Services,
    // because Orleans resolves IAttributeToFactoryMapper<T> only from services
    // registered within the silo-builder scope before Build() is called.
    public static ISiloBuilder UseInoNeuron(this ISiloBuilder silo)
    {
        silo.Services.AddSingleton<ICortexCapability, CortexCapability>();
        LlmAttributeMapperRegistration.RegisterAllAttributeMappers(silo.Services);
        return silo;
    }

    // Convenience overload for test configurators (ISiloConfigurator.Configure)
    // and any IServiceCollection context where ISiloBuilder is not available.
    public static IServiceCollection AddInoNeuron(this IServiceCollection services)
    {
        services.AddSingleton<ICortexCapability, CortexCapability>();
        LlmAttributeMapperRegistration.RegisterAllAttributeMappers(services);
        return services;
    }
}
