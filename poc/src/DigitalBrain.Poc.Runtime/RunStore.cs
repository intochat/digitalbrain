using System.Text.Json;

namespace DigitalBrain.Poc.Runtime;

internal sealed class RunStore
{
    private static readonly Dictionary<string, SemaphoreSlim> Locks =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly object LocksGate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate;

    public RunStore(PocDataRoot root)
    {
        _path = root.StorePath;
        lock (LocksGate)
        {
            if (!Locks.TryGetValue(_path, out _gate!))
            {
                _gate = new SemaphoreSlim(1, 1);
                Locks.Add(_path, _gate);
            }
        }
    }

    public async Task<TResult> ReadAsync<TResult>(
        Func<RunDocument, TResult> read,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return read(await LoadAsync(cancellationToken));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TResult> TransactAsync<TResult>(
        Func<RunDocument, Task<(TResult Result, bool Commit)>> transaction,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadAsync(cancellationToken);
            var (result, commit) = await transaction(document);
            if (commit)
            {
                await SaveAsync(document, cancellationToken);
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal Task BindCandidateModuleIdentitiesAsync(
        IEnumerable<CandidateModuleBinding> bindings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var requested = bindings.ToArray();
        return TransactAsync(
            document =>
            {
                var changed = false;
                foreach (var binding in requested)
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(binding.OwnerId);
                    ArgumentException.ThrowIfNullOrWhiteSpace(binding.Family);
                    ArgumentException.ThrowIfNullOrWhiteSpace(binding.Revision);
                    ArgumentNullException.ThrowIfNull(binding.Identity);
                    var matches = document.CandidateModuleBindings
                        .Where(existing =>
                            string.Equals(existing.OwnerId, binding.OwnerId, StringComparison.Ordinal) &&
                            string.Equals(existing.Family, binding.Family, StringComparison.Ordinal) &&
                            string.Equals(existing.Revision, binding.Revision, StringComparison.Ordinal))
                        .ToArray();
                    if (matches.Length > 1)
                    {
                        throw new InvalidDataException(
                            "The durable module registry contains duplicate immutable module bindings.");
                    }

                    if (matches.Length == 1)
                    {
                        if (matches[0].Identity != binding.Identity)
                        {
                            throw new InvalidDataException(
                                "The supplied module does not match the durable immutable module identity for its owner, family, and revision.");
                        }

                        continue;
                    }

                    document.CandidateModuleBindings.Add(binding);
                    changed = true;
                }

                return Task.FromResult((true, changed));
            },
            cancellationToken);
    }

    internal Task BindTrustedInputDeliveriesAsync(
        string ownerId,
        string receiptId,
        IEnumerable<string> deliveryIds,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptId);
        ArgumentNullException.ThrowIfNull(deliveryIds);
        var requested = deliveryIds.Order(StringComparer.Ordinal).ToArray();
        var key = ownerId + "\n" + receiptId;
        return TransactAsync(
            document =>
            {
                if (document.TrustedInputDeliveries.TryGetValue(key, out var existing))
                {
                    if (!existing.Order(StringComparer.Ordinal).SequenceEqual(
                        requested,
                        StringComparer.Ordinal))
                    {
                        throw new InvalidDataException(
                            "A trusted input receipt cannot be rebound to different durable deliveries.");
                    }

                    return Task.FromResult((true, false));
                }

                document.TrustedInputDeliveries.Add(key, requested.ToList());
                return Task.FromResult((true, true));
            },
            cancellationToken);
    }

    private async Task<RunDocument> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new RunDocument();
        }

        await using var stream = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<RunDocument>(
                stream,
                JsonOptions,
                cancellationToken) ??
            throw new InvalidDataException($"The durable POC store is empty: {_path}");
    }

    private async Task SaveAsync(RunDocument document, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
