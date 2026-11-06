using System.Reflection;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Runtime.Orleans.Compute;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Signalling;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Runtime.Synapses;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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
    /// signal publishing and synapse retention. Relies on the topology's <see cref="RuntimeOptions.SignalStreams"/> binding.
    /// </summary>
    /// <exception cref="InvalidOperationException">The runtime was already added to this silo.</exception>
    public static ISiloBuilder AddDigitalBrainRuntime(this ISiloBuilder silo, params Assembly[] modules)
        => silo.AddDigitalBrainRuntime(null, modules);

    /// <summary>
    /// <see cref="AddDigitalBrainRuntime(ISiloBuilder, Assembly[])"/> with the key and store names the host chose. A null
    /// <paramref name="options"/> keeps the options the host registered with <see cref="TopologyHosting.UseRuntimeOptions"/>, or
    /// <see cref="RuntimeOptions.Default"/> when it registered none.
    /// </summary>
    /// <exception cref="InvalidOperationException">The runtime was already added to this silo.</exception>
    public static ISiloBuilder AddDigitalBrainRuntime(this ISiloBuilder silo, RuntimeOptions? options, params Assembly[] modules)
    {
        ArgumentNullException.ThrowIfNull(silo);
        ArgumentNullException.ThrowIfNull(modules);
        if (silo.Services.Any(descriptor => descriptor.ServiceType == typeof(RuntimeAdded)))
        {
            throw new InvalidOperationException("AddDigitalBrainRuntime was already called on this silo; ConfigureTopologySilo calls it once.");
        }
        silo.Services.AddSingleton(new RuntimeAdded());
        if (options is not null)
        {
            silo.Services.RemoveAll<RuntimeOptions>();
            silo.Services.AddSingleton(options);
        }
        else { silo.Services.TryAddSingleton(RuntimeOptions.Default); }
        silo.AddNeuronHosting(modules);
        silo.Services.TryAddSingleton<IReadOnlyList<ActionDescriptor>>(sp =>
        {
            var fromTopology = sp.GetService<TopologyContext>()?.Modules.SelectMany(m => m.Actions) ?? [];
            var fromRegistry = sp.GetRequiredService<NeuronTypeRegistry>().Actions;
            return [.. fromTopology.Concat(fromRegistry).DistinctBy(a => a.Name, StringComparer.Ordinal)];
        });
        // The live catalog, seeded from the startup scan; state neurons, the installs neuron and the gate index read it per call.
        silo.Services.TryAddSingleton(sp => new SynapseCatalogSource(sp.GetRequiredService<NeuronTypeRegistry>().Catalog));
        silo.Services.TryAddSingleton<ISynapseCatalogSource>(sp => sp.GetRequiredService<SynapseCatalogSource>());
        silo.Services.TryAddSingleton(sp => new InstalledGateIndex(sp.GetRequiredService<ISynapseCatalogSource>()));
        silo.Services.TryAddSingleton<DigitalBrain.Spi.IGateIndex>(sp => sp.GetRequiredService<InstalledGateIndex>());
        silo.Services.TryAddSingleton<InstalledGates>();
        silo.Services.TryAddSingleton<DigitalBrain.Spi.IInstallHooks, OrleansInstallHooks>();
        silo.Services.TryAddSingleton<DigitalBrain.Spi.ISynapseRegistrar, OrleansSynapseRegistrar>();
        // The synapse host: compiles, loads and deploys generated sources; each brain's sources neuron keeps them, and the restorer
        // brings one back when an activation or the gate path misses its alias (as after a restart).
        silo.Services.TryAddSingleton(_ => new SynapseCompiler(ReferenceSet.FromDefaultContext()));
        silo.Services.TryAddSingleton<SynapseLoader>();
        silo.Services.TryAddSingleton<ISourceStore>(sp => new NeuronSourceStore(
            brain => NeuronHosting.Get<ISources>(sp, new NeuronId(brain, SourcesType, "default"))));
        silo.Services.TryAddSingleton(sp => new SynapseHost(
            sp.GetRequiredService<SynapseCompiler>(),
            sp.GetRequiredService<SynapseLoader>(),
            sp.GetRequiredService<DigitalBrain.Spi.ISynapseRegistrar>(),
            sp.GetRequiredService<ISynapseCatalogSource>(),
            brain => NeuronHosting.Get<IInstalls>(sp, new NeuronId(brain, OrleansInstallHooks.InstallsType, "default")),
            sp.GetRequiredService<ISourceStore>(),
            sp.GetRequiredService<DigitalBrain.Spi.IClusterListeners>(),
            sp.GetService<ILogger<SynapseHost>>()));
        silo.Services.TryAddSingleton<ISynapseHost>(sp => sp.GetRequiredService<SynapseHost>());
        silo.Services.TryAddSingleton<DigitalBrain.Spi.ISynapseRestorer>(sp => new SynapseRestorer(
            sp.GetRequiredService<SynapseLoader>(),
            sp.GetRequiredService<DigitalBrain.Spi.ISynapseRegistrar>(),
            sp.GetRequiredService<ISourceStore>(),
            sp.GetRequiredService<ISynapseCatalogSource>(),
            sp.GetRequiredService<SynapseHost>()));
        silo.Services.AddJournalPayloads(modules);
        silo.Services.AddSignalPublisher();
        silo.Services.TryAddSingleton<DigitalBrain.Spi.ISlotResolver, BrokerSlotResolver>();
        silo.Services.AddSingleton<SynapseCounters>();
        silo.AddSynapseRetention();
        silo.AddRuntimeServices();
        silo.Services.AddCapabilityBroker();
        silo.Services.TryAddSingleton<Operations.IConfirmationContexts, Confirmations.KernelConfirmations>();
        silo.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IOutgoingGrainCallFilter, TraceOutgoingFilter>());
        return silo;
    }

    // The alias of the sources neuron NeuronTypeRegistry hosts on every silo.
    private const string SourcesType = "synapse.sources";

    private sealed class RuntimeAdded;
}
