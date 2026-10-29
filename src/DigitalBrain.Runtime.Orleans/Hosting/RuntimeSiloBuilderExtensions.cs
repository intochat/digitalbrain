using System.Reflection;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Signalling;
using DigitalBrain.Runtime.Orleans.Synapses;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>
/// The single composition entry of the DigitalBrain runtime on a silo. <see cref="TopologyHosting.ConfigureTopologySilo"/> calls it
/// once with the module assemblies given to <c>UseTopology</c>; topology classes never call it.
/// </summary>
public static class RuntimeSiloBuilderExtensions
{
    /// <summary>
    /// Hosts the plain <c>[Neuron]</c> classes of <paramref name="modules"/> on the host grains, with their JSON journal payloads,
    /// signal publishing and synapse retention. Relies on the topology's <see cref="IntoChatOSNames.SignalStreams"/> binding.
    /// </summary>
    /// <exception cref="InvalidOperationException">The runtime was already added to this silo.</exception>
    public static ISiloBuilder AddDigitalBrainRuntime(this ISiloBuilder silo, params Assembly[] modules)
    {
        ArgumentNullException.ThrowIfNull(silo);
        ArgumentNullException.ThrowIfNull(modules);
        if (silo.Services.Any(descriptor => descriptor.ServiceType == typeof(RuntimeAdded)))
        {
            throw new InvalidOperationException("AddDigitalBrainRuntime was already called on this silo; ConfigureTopologySilo calls it once.");
        }
        silo.Services.AddSingleton(new RuntimeAdded());
        silo.AddNeuronHosting(modules);
        silo.Services.AddJournalPayloads(modules);
        silo.Services.AddSignalPublisher();
        silo.Services.AddSingleton<SynapseCounters>();
        silo.Services.AddSingleton<SynapseReconcile>();
        silo.Services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>>(sp => sp.GetRequiredService<SynapseReconcile>());
        silo.AddSynapseRetention();
        silo.AddRuntimeServices();
        silo.Services.AddCapabilityBroker();
        return silo;
    }

    private sealed class RuntimeAdded;
}
