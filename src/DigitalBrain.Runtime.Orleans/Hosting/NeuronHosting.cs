using System.Reflection;
using DigitalBrain.Invocation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans;
using Orleans.Hosting;

namespace DigitalBrain.Runtime.Orleans.Hosting;

/// <summary>The internal registration of the host grains' type map. AddDigitalBrainRuntime (04-11) calls it; it is not a public entry.</summary>
internal static class NeuronHosting
{
    /// <summary>Hosts the plain <see cref="NeuronAttribute"/> classes of <paramref name="assemblies"/>.</summary>
    internal static ISiloBuilder AddNeuronHosting(this ISiloBuilder silo, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(silo);
        ArgumentNullException.ThrowIfNull(assemblies);
        RuntimeProtocol.Register(Signalling.RuntimeOrleansJsonContext.Default);
        foreach (var assembly in assemblies) { silo.Services.AddSingleton(new HostedAssembly(assembly)); }
        silo.Services.TryAddSingleton(sp => new NeuronTypeRegistry(sp.GetServices<HostedAssembly>().Select(hosted => hosted.Assembly)));
        return silo;
    }

    /// <summary>A typed proxy, from outside any neuron, to the neuron <paramref name="id"/> hosted in the silo of <paramref name="services"/>.</summary>
    internal static T Get<T>(IServiceProvider services, NeuronId id) where T : class
        => NeuronProxy.Create<T>(id, new OrleansInvocationSink(services.GetRequiredService<IGrainFactory>(), services.GetService<NeuronTypeRegistry>(), null,
            services.GetService<NeuronStreamAuthorizer>() is { } gate ? (target, call) => gate.Authorize(services, target, call) : null));

    internal sealed record HostedAssembly(Assembly Assembly);
}
