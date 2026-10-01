using System.Collections.Concurrent;
using Orleans.Runtime;
using Orleans.Storage;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

// Grain storage that fails the next write matching FailNextWrite, to prove commands survive a lost save.
internal sealed class FlakyGrainStorage : IGrainStorage
{
    private readonly ConcurrentDictionary<(string StateName, GrainId GrainId), object?> _states = new();

    public Func<object?, bool>? FailNextWrite { get; set; }

    public Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        if (_states.TryGetValue((stateName, grainId), out var state))
        {
            grainState.State = (T)state!;
            grainState.RecordExists = true;
            grainState.ETag = "stored";
        }
        return Task.CompletedTask;
    }

    public Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        if (FailNextWrite is { } fails && fails(grainState.State))
        {
            FailNextWrite = null;
            throw new IOException("Injected storage failure.");
        }
        _states[(stateName, grainId)] = grainState.State;
        grainState.RecordExists = true;
        grainState.ETag = "stored";
        return Task.CompletedTask;
    }

    public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        _states.TryRemove((stateName, grainId), out _);
        grainState.RecordExists = false;
        grainState.ETag = null;
        return Task.CompletedTask;
    }
}
