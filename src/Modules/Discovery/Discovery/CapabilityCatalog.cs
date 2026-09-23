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
    private bool _built;

    public bool Degraded { get; private set; }

    public async Task RebuildAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<AppManifest> manifests;
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
            _built = true;
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
                .Select(static entry => new CapabilityVectorRecord(entry.Id, string.Empty, entry.Text, entry.Embedding))
                .ToArray();
            await vectors.UpsertAsync(CapabilityCollection.SystemCapabilities, records, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Persisting capability vectors failed; in-memory search is unaffected.");
        }
    }

    public async ValueTask<CapabilitySearchResult> SearchAsync(string query, int take, CancellationToken cancellationToken)
    {
        if (!_built)
        {
            await RebuildAsync(cancellationToken).ConfigureAwait(false);
        }

        return await _index.SearchAsync(query, take, Degraded, cancellationToken).ConfigureAwait(false);
    }

    private static string Signature(IReadOnlyList<AppManifest> manifests)
    {
        var builder = new StringBuilder();
        foreach (var manifest in manifests.OrderBy(static manifest => manifest.Id, StringComparer.Ordinal))
        {
            builder.Append(manifest.Id).Append('|').Append(manifest.Version).Append('|')
                .Append(manifest.Name).Append('|').Append(manifest.DescriptionForModel).Append(';');
            foreach (var operation in manifest.Operations.OrderBy(static operation => operation.Name, StringComparer.Ordinal))
            {
                builder.Append(operation.Name).Append('|').Append(operation.DescriptionForModel).Append(';');
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
