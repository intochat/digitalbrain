using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Runtime.Orleans.Hosting;

/// <summary>The silo and cluster-client <see cref="INeuronProxies"/>: a typed proxy for a <see cref="NeuronId"/>.</summary>
internal sealed class OrleansNeuronProxies(IServiceProvider services) : INeuronProxies
{
    public T Get<T>(NeuronId id) where T : class => NeuronHosting.Get<T>(services, id);
}

/// <summary>Registers <see cref="INeuronProxies"/> for a host that is outside any neuron.</summary>
public static class NeuronProxyHosting
{
    /// <summary>Resolves neurons through the host grains. Idempotent.</summary>
    public static IServiceCollection AddNeuronProxies(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<INeuronProxies, OrleansNeuronProxies>();
        return services;
    }
}
