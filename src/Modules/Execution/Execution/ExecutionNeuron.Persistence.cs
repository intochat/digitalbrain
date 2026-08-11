namespace DigitalBrain.Execution;

public sealed partial class ExecutionNeuron
{
    private ExecutionData Load()
    {
        if (LoadIfStarted() is not { } data)
        {
            throw new InvalidOperationException($"Execution '{Id}' has not been started.");
        }

        return data;
    }

    private ExecutionData? LoadIfStarted()
        => _state.Value is { Length: > 0 } serialized
            ? _states.Deserialize(serialized)
            : null;

    private void Stage(ExecutionData data)
        => _state.Value = _states.SerializeToArray(data);

    private void StageForTurn(ExecutionData data)
    {
        var previous = _state.Value is { Length: > 0 } serialized
            ? serialized.ToArray()
            : [];
        Stage(data);
        EnlistTurnRollback(() => _state.Value = previous);
    }

    private async Task SaveAsync(ExecutionData data)
    {
        Stage(data);
        await WriteStateAsync().ConfigureAwait(true);
    }
}
