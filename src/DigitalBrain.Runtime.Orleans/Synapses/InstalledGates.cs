using System.Collections.Concurrent;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>
/// The per-brain cache over the silo's <see cref="InstalledGateIndex"/>: a brain's installs are read from its
/// <see cref="IInstalls"/> neuron (<see cref="IInstalls.List"/>) the first time a gate is looked up for it, and again after it is
/// invalidated, by a <see cref="GateChanged"/> heard through <see cref="IClusterListeners"/> or by <see cref="OrleansInstallHooks"/>
/// on the silo that ran the install.
/// </summary>
/// <remarks>
/// A silo hears the <see cref="GateChanged"/> of the neurons it hosts (<see cref="IClusterListeners"/>), so on a cluster another
/// silo's cache of a brain stays as it was until it is reloaded; brain-affine placement keeps a brain's neurons on one silo.
/// </remarks>
internal sealed class InstalledGates(InstalledGateIndex index, IServiceProvider services)
{
    private const string Listener = "runtime.installed-gates";

    private readonly ConcurrentDictionary<BrainId, long> _generation = new();
    private readonly ConcurrentDictionary<BrainId, long> _loaded = new();
    private int _listening;

    /// <summary>Reads the installs of <paramref name="brain"/> into the index unless they are current.</summary>
    /// <param name="brain">The brain about to look up gates.</param>
    /// <returns>A task that completes when the index serves the brain's installs.</returns>
    public async Task Load(BrainId brain)
    {
        if (Interlocked.Exchange(ref _listening, 1) == 0)
        {
            await services.GetRequiredService<IClusterListeners>().Listen<GateChanged>(Listener, (publisher, _, _) =>
            {
                Invalidate(publisher.Brain);
                return Task.CompletedTask;
            }).ConfigureAwait(true);
        }
        var generation = _generation.GetOrAdd(brain, 0);
        if (_loaded.TryGetValue(brain, out var loaded) && loaded == generation) { return; }
        var installs = NeuronHosting.Get<IInstalls>(services, new NeuronId(brain, OrleansInstallHooks.InstallsType, "default"));
        var records = await installs.List(CancellationToken.None).ConfigureAwait(true);
        // An invalidation during the read leaves the brain unloaded, so the next lookup reads again.
        if (_generation.GetValueOrDefault(brain) != generation) { return; }
        index.Refresh(brain, records);
        _loaded[brain] = generation;
    }

    /// <summary>Marks the installs of <paramref name="brain"/> stale: the next gate lookup reads them again.</summary>
    /// <param name="brain">The brain whose installs changed.</param>
    public void Invalidate(BrainId brain) => _generation.AddOrUpdate(brain, 1, (_, generation) => generation + 1);
}
