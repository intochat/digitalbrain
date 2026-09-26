using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Apps;
using DigitalBrain.Discovery.Search;
using DigitalBrain.Discovery.Vector;
using DigitalBrain.Contracts.Registry;
using DigitalBrain.Core.Registry;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Discovery;

// Manifests are the truth. The catalog is rebuilt idempotently and falls back to keyword search.
internal sealed class CapabilityCatalog(
    IEnumerable<IManifestSource> sources,
    INeuronRegistry selectedRegistry,
    IGrainFactory grains,
    ICapabilityEmbedder embedder,
    ICapabilityVectorIndex vectors,
    ILogger<CapabilityCatalog> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CapabilityIndex _index = CapabilityIndex.Empty;
    private string? _signature;
    private bool _indexDegraded;
    private volatile bool _dirty = true;

    public bool Degraded { get; private set; }

    // A manifest changed: the next search re-reads the sources. Between changes a search reads
    // the already-built index, so a turn never pays a manifest round-trip.
    public void Invalidate() => _dirty = true;

    public async Task RebuildAsync(CancellationToken cancellationToken)
    {
        var read = new List<ScopedAppManifest>();
        var unreadable = 0;
        foreach (var source in sources)
        {
            try
            {
                read.AddRange(await source.ReadAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Capability manifests from {Source} could not be read; indexing the others.", source.GetType().Name);
                unreadable++;
            }
        }

        var registry = await grains.GetGrain<INeuronRegistryGrain>(selectedRegistry.Version)
            .Read().ConfigureAwait(false);
        var neurons = registry.Where(static item => item.AgentRoutable).ToArray();
        if (unreadable > 0 && read.Count == 0 && (_signature is not null || neurons.Length == 0))
        {
            Degraded = true;
            return;
        }

        // First-party manifests and saved apps come from different sources; a later source wins per app and scope.
        IReadOnlyList<ScopedAppManifest> manifests = read
            .GroupBy(static entry => (entry.Manifest.Id, entry.OwningWorkspaceId))
            .Select(static group => group.Last())
            .ToList();

        var signature = Signature(manifests, neurons);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (signature == _signature)
            {
                _dirty = unreadable > 0 && read.Count == 0;
                Degraded = _indexDegraded || _dirty;
                return;
            }

            var degraded = false;
            async ValueTask<float[]?> Embed(string text, CancellationToken token)
            {
                try
                {
                    return await embedder.EmbedAsync(text, token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    degraded = true;
                    logger.LogWarning(exception, "Capability embedding failed; discovery falls back to keyword search.");
                    return null;
                }
            }

            _index = await CapabilityIndex.BuildAsync(manifests, Embed, cancellationToken, neurons).ConfigureAwait(false);
            await PersistAsync(_index, cancellationToken).ConfigureAwait(false);
            _signature = signature;
            _indexDegraded = degraded;
            Degraded = degraded || unreadable > 0 && read.Count == 0;
            _dirty = unreadable > 0 && read.Count == 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PersistAsync(CapabilityIndex index, CancellationToken cancellationToken)
    {
        try
        {
            var records = index.EmbeddedEntries()
                .Select(static entry => new CapabilityVectorRecord(
                    entry.WorkspaceId.Length == 0 ? entry.Id : entry.WorkspaceId + "/" + entry.Id,
                    entry.WorkspaceId,
                    entry.Text,
                    entry.Embedding))
                .ToArray();
            await vectors.UpsertAsync(CapabilityCollection.SystemCapabilities, records, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Persisting capability vectors failed; in-memory search is unaffected.");
        }
    }

    public async ValueTask<CapabilitySearchResult> SearchAsync(string query, string? workspaceId, int take, CancellationToken cancellationToken, bool appsOnly = false)
    {
        // The index is rebuilt at startup and whenever a manifest-change signal invalidates it, so
        // a per-turn search is only a read; a stale or never-built index rebuilds on the first use.
        if (_dirty)
        {
            await RebuildAsync(cancellationToken).ConfigureAwait(false);
        }

        return await _index.SearchAsync(query, workspaceId, take, Degraded, cancellationToken, appsOnly).ConfigureAwait(false);
    }

    private static string Signature(IReadOnlyList<ScopedAppManifest> manifests, IReadOnlyList<NeuronRegistration> neurons)
    {
        var builder = new StringBuilder();
        foreach (var scoped in manifests.OrderBy(static scoped => scoped.Manifest.Id, StringComparer.Ordinal))
        {
            var manifest = scoped.Manifest;
            builder.Append(manifest.Id).Append('|').Append(scoped.Scope).Append('|').Append(scoped.WorkspaceId).Append('|')
                .Append(manifest.Version).Append('|')
                .Append(manifest.Name).Append('|').Append(manifest.DescriptionForModel).Append(';');
            foreach (var operation in manifest.Operations.OrderBy(static operation => operation.Name, StringComparer.Ordinal))
            {
                builder.Append(operation.Name).Append('|').Append(operation.DescriptionForModel).Append(';');
            }
        }

        foreach (var neuron in neurons.OrderBy(static item => item.Id, StringComparer.Ordinal))
        {
            builder.Append(neuron.Id).Append('|').Append(neuron.ModuleId).Append('|')
                .Append(neuron.Name).Append('|').Append(neuron.Description).Append(';');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
