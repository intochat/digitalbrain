using System.Collections.Concurrent;

namespace DigitalBrain.Units;

/// <summary>
/// Silo-local concurrent cache tracking the latest live build hash per (brain, sourceKey).
/// Used by host grains in Drain() to decide whether to deactivate on idle so old activations gracefully reload.
/// </summary>
public sealed class LiveBindings
{
    private readonly ConcurrentDictionary<(BrainId Brain, string SourceKey), string> _current = new();

    /// <summary>Records the live build hash for a brain's source key.</summary>
    public void Set(BrainId brain, BuildRef build)
    {
        _current[(brain, build.SourceKey)] = build.Hash;
    }

    /// <summary>Marks a retired source key so active grains can detect retirement and deactivate.</summary>
    public void Retire(BrainId brain, string sourceKey)
    {
        _current[(brain, sourceKey)] = string.Empty;
    }

    /// <summary>True when no newer hash is known for the source key or the build matches the current hash.</summary>
    public bool IsCurrent(BrainId brain, BuildRef build)
    {
        if (!_current.TryGetValue((brain, build.SourceKey), out var currentHash))
        {
            return true; // Untracked / cold cache: allow normal execution
        }
        return !string.IsNullOrEmpty(currentHash) && string.Equals(currentHash, build.Hash, StringComparison.Ordinal);
    }

    /// <summary>Looks up the current live hash for the given brain and source key, if recorded.</summary>
    public bool TryGet(BrainId brain, string sourceKey, out string hash)
    {
        if (_current.TryGetValue((brain, sourceKey), out var found) && !string.IsNullOrEmpty(found))
        {
            hash = found;
            return true;
        }
        hash = string.Empty;
        return false;
    }
}
