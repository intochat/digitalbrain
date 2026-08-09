using DigitalBrain.Poc.Runtime;
using System.Text.Json;

namespace DigitalBrain.Poc.ControlPlane;

public sealed class FileCandidateFamilyRegistry : ICandidateFamilyRegistry
{
    private static readonly Dictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly object GatesLock = new();
    private readonly string _catalogPath;
    private readonly string _lockPath;
    private readonly SemaphoreSlim _gate;

    public FileCandidateFamilyRegistry(PocDataRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _catalogPath = Path.Combine(root.ControlPlaneRoot, "candidate-families.json");
        _lockPath = Path.Combine(root.ControlPlaneRoot, "candidate-families.lock");
        lock (GatesLock)
        {
            if (!Gates.TryGetValue(_catalogPath, out _gate!))
            {
                _gate = new SemaphoreSlim(1, 1);
                Gates.Add(_catalogPath, _gate);
            }
        }
    }

    public async ValueTask<bool> TryReserveAsync(
        AuthenticatedPrincipal owner,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_catalogPath)!);
            await using var processLock = await AcquireProcessLockAsync(cancellationToken);
            var existing = await ReadAsync(cancellationToken);
            if (!existing.TryAdd(family.Value, owner.OwnerId))
            {
                return false;
            }

            var temporary = _catalogPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(
                    temporary,
                    JsonSerializer.SerializeToUtf8Bytes(
                        existing.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)),
                    cancellationToken);
                File.Move(temporary, _catalogPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<bool> IsReservedForAsync(
        AuthenticatedPrincipal owner,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_catalogPath)!);
            await using var processLock = await AcquireProcessLockAsync(cancellationToken);
            var existing = await ReadAsync(cancellationToken);
            return existing.TryGetValue(family.Value, out var ownerId) &&
                string.Equals(ownerId, owner.OwnerId, StringComparison.Ordinal);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, string>> ReadAsync(CancellationToken cancellationToken) =>
        !File.Exists(_catalogPath)
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(
                JsonSerializer.Deserialize<Dictionary<string, string>>(
                    await File.ReadAllBytesAsync(_catalogPath, cancellationToken)) ??
                    throw new InvalidDataException("The candidate-family reservation catalog is empty."),
                StringComparer.Ordinal);

    private async Task<FileStream> AcquireProcessLockAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    _lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
            }
        }
    }
}
