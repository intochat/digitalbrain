using System.Collections.Concurrent;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>In-memory grain storage scoped to a host path, surviving in-process restarts without disk I/O.</summary>
internal sealed class LocalMemoryGrainStorage : IGrainStorage
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, (object? State, string ETag)>> Stores = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, (object? State, string ETag)> _states;

    public LocalMemoryGrainStorage(string scope, string name)
    {
        _states = Stores.GetOrAdd($"{scope}|{name}", _ => new(StringComparer.Ordinal));
    }

    public Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        ArgumentNullException.ThrowIfNull(grainState);
        var key = $"{grainId}|{stateName}";
        if (_states.TryGetValue(key, out var record) && record.State is T typed)
        {
            grainState.State = typed;
            grainState.ETag = record.ETag;
            grainState.RecordExists = true;
        }
        else
        {
            grainState.State = Activator.CreateInstance<T>()!;
            grainState.ETag = null;
            grainState.RecordExists = false;
        }
        return Task.CompletedTask;
    }

    public Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        ArgumentNullException.ThrowIfNull(grainState);
        var key = $"{grainId}|{stateName}";
        var nextEtag = Guid.NewGuid().ToString("N");
        _states[key] = (grainState.State, nextEtag);
        grainState.ETag = nextEtag;
        grainState.RecordExists = true;
        return Task.CompletedTask;
    }

    public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        ArgumentNullException.ThrowIfNull(grainState);
        var key = $"{grainId}|{stateName}";
        _states.TryRemove(key, out _);
        grainState.State = Activator.CreateInstance<T>()!;
        grainState.ETag = null;
        grainState.RecordExists = false;
        return Task.CompletedTask;
    }
}
