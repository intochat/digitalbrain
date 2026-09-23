using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Apps;
using DigitalBrain.Discovery.Search;
using DigitalBrain.Discovery.Vector;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Discovery;

// Manifests are the truth. The catalog is rebuilt idempotently and falls back to keyword search.
internal sealed class CapabilityCatalog(
    IManifestSource source,
    ICapabilityEmbedder embedder,
    ICapabilityVectorIndex vectors,
    ILogger<CapabilityCatalog> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CapabilityIndex _index = CapabilityIndex.Empty;
    private string? _signature;

    public bool Degraded { get; private set; }

    public async Task RebuildAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ScopedAppManifest> manifests;
        try
        {
            manifests = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Capability manifests could not be read; keeping the previous catalog.");
            Degraded = true;
            return;
        }

        var signature = Signature(manifests);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (signature == _signature)
            {
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

            _index = await CapabilityIndex.BuildAsync(manifests, Embed, cancellationToken).ConfigureAwait(false);
            await PersistAsync(_index, cancellationToken).ConfigureAwait(false);
            _signature = signature;
            Degraded = degraded;
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

    public async ValueTask<CapabilitySearchResult> SearchAsync(string query, string? workspaceId, int take, CancellationToken cancellationToken)
    {
        // Cheap idempotent rebuild: re-reads the manifest source and re-indexes only when it changed,
        // so an app saved after startup becomes searchable on the next turn.
        await RebuildAsync(cancellationToken).ConfigureAwait(false);
        return await _index.SearchAsync(query, workspaceId, take, Degraded, cancellationToken).ConfigureAwait(false);
    }

    private static string Signature(IReadOnlyList<ScopedAppManifest> manifests)
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

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
