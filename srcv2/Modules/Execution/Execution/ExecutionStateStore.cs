using DigitalBrain.Abstractions;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Execution;

internal sealed class ExecutionStateStore(
    IDurableValue<byte[]> state,
    Serializer<ExecutionData> serializer)
{
    internal bool HasStarted => state.Value is { Length: > 0 };

    internal ExecutionData Load(NeuronId execution)
    {
        if (LoadIfStarted() is not { } data)
        {
            throw new InvalidOperationException($"Execution '{execution}' has not been started.");
        }

        return data;
    }

    internal ExecutionData? LoadIfStarted()
        => state.Value is { Length: > 0 } serialized
            ? serializer.Deserialize(serialized)
            : null;

    internal void Stage(ExecutionData data)
        => state.Value = serializer.SerializeToArray(data);

    internal void StageForTurn(ExecutionData data, Action<Action> enlistRollback)
    {
        var previous = state.Value is { Length: > 0 } serialized
            ? serialized.ToArray()
            : [];
        Stage(data);
        enlistRollback(() => state.Value = previous);
    }

    internal async Task SaveAsync(ExecutionData data, Func<ValueTask> writeStateAsync)
    {
        Stage(data);
        await writeStateAsync().ConfigureAwait(true);
    }
}
