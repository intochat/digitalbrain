using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.Kernel.Runtime;

// Keeps persisted .ino bodies out of startup discovery. Metadata registers a
// local source key; first activation loads, validates and then reuses its text.
public sealed class InoDefinitionCache
{
    readonly ConcurrentDictionary<string, string> _paths =
        new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, Lazy<Task<string>>> _definitions =
        new(StringComparer.Ordinal);

    public void RegisterFile(string key, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        if (_paths.TryGetValue(key, out var existingPath)
            && string.Equals(existingPath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _paths[key] = fullPath;
        _definitions.TryRemove(key, out _);
    }

    public async ValueTask<string> GetSourceAsync(
        NeuronDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(descriptor.InoLangSource))
            return descriptor.InoLangSource;

        if (string.IsNullOrWhiteSpace(descriptor.InoLangSourceCacheKey)
            || !_paths.TryGetValue(descriptor.InoLangSourceCacheKey, out var path))
        {
            throw new InoDefinitionNotRegisteredException(descriptor.Fqn);
        }

        // The shared load itself is not canceled by one caller; canceled
        // activations stop waiting while another activation can complete it.
        var load = _definitions.GetOrAdd(
            descriptor.InoLangSourceCacheKey,
            _ => new Lazy<Task<string>>(
                () => File.ReadAllTextAsync(path),
                LazyThreadSafetyMode.ExecutionAndPublication));

        string source;
        try
        {
            source = await load.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            _definitions.TryRemove(descriptor.InoLangSourceCacheKey, out _);
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            _definitions.TryRemove(descriptor.InoLangSourceCacheKey, out _);
            throw;
        }

        if (descriptor.InoLangSourceSha256 is { Length: > 0 } expected
            && !string.Equals(ComputeHash(source), expected, StringComparison.OrdinalIgnoreCase))
        {
            _definitions.TryRemove(descriptor.InoLangSourceCacheKey, out _);
            throw new InvalidDataException(
                $"The .ino definition for '{descriptor.Fqn}' no longer matches its promoted manifest.");
        }

        return source;
    }

    public static string ComputeHash(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }
}

internal sealed class InoDefinitionNotRegisteredException(string fqn)
    : Exception($"No .ino definition was registered for '{fqn}'.");
