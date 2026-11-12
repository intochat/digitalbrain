namespace DigitalBrain.Units;

/// <summary>
/// Single-point resolution service mapping (brain, alias) to executable <see cref="UnitHandle"/>.
/// Resolves through the durable sources directory and unit deployment aggregates.
/// </summary>
public sealed class UnitResolver(
    IUnitLoader loader,
    ISynapseCatalogSource catalogs,
    LiveBindings live,
    Func<NeuronId, ISources> sources,
    Func<NeuronId, IUnitDeployment> deployments,
    Func<BrainId, IDisposable>? stampPlatform = null)
{
    /// <summary>Resolves a state alias for the specified brain and view (Live or Proposed) to a refcounted unit handle.</summary>
    public async Task<UnitHandle?> Resolve(BrainId brain, string alias, UnitView view, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(alias);
        using var stamp = stampPlatform?.Invoke(brain);

        var sourcesNeuron = sources(new NeuronId(brain, "synapse.sources", "default"));
        var sourceKey = await sourcesNeuron.OwnerOf(alias, ct).ConfigureAwait(true);
        if (sourceKey is null)
        {
            if (view == UnitView.Live && catalogs.Static.States.ContainsKey(alias))
            {
                return UnitHandle.Static(catalogs.Static);
            }

            return null;
        }

        var deployment = deployments(new NeuronId(brain, "synapse.deployment", sourceKey));
        var buildRef = await deployment.Ref(view, ct).ConfigureAwait(true);
        if (buildRef is null)
        {
            return null;
        }

        if (await deployment.Build(buildRef.Value.Hash, ct).ConfigureAwait(true) is not { RebuildReason: null } stored)
        {
            return null;
        }

        var handle = loader.TryAcquire(buildRef.Value);
        if (handle is null)
        {
            try
            {
                handle = loader.Acquire(stored.Image);
            }
            catch (UnitRefusedException ex)
            {
                await deployment.Refuse(buildRef.Value.Hash, ex.Disallowed, ct).ConfigureAwait(true);
                return null;
            }
        }

        if (!handle.Catalog.States.ContainsKey(alias))
        {
            handle.Dispose();
            return null;
        }

        if (view == UnitView.Live)
        {
            live.Set(brain, buildRef.Value);
        }

        return handle;
    }

    /// <summary>Returns catalogs for all live units deployed to the brain, plus the static catalog.</summary>
    public async Task<IReadOnlyList<SynapseCatalog>> Catalogs(BrainId brain, CancellationToken ct = default)
    {
        using var stamp = stampPlatform?.Invoke(brain);
        var sourcesNeuron = sources(new NeuronId(brain, "synapse.sources", "default"));
        var keys = await sourcesNeuron.Keys(ct).ConfigureAwait(true);
        var result = new List<SynapseCatalog> { catalogs.Static };
        foreach (var key in keys)
        {
            BuildRef? buildRef = live.TryGet(brain, key, out var liveHash) ? new BuildRef(key, liveHash) : null;
            if (buildRef is null)
            {
                var deployment = deployments(new NeuronId(brain, "synapse.deployment", key));
                buildRef = await deployment.Ref(UnitView.Live, ct).ConfigureAwait(true);
            }
            if (buildRef is null) { continue; }
            using var handle = loader.TryAcquire(buildRef.Value);
            if (handle is not null)
            {
                result.Add(handle.Catalog);
            }
        }
        return result;
    }
}
