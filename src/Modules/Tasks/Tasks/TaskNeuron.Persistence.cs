
namespace DigitalBrain.Tasks;

public sealed partial class TaskNeuron
{
    private TaskData Load()
    {
        if (LoadIfStarted() is not { } data)
        {
            throw new InvalidOperationException($"Task '{Id}' has not been started.");
        }

        return data;
    }

    private TaskData? LoadIfStarted()
        => _state.Value is { Length: > 0 } serialized
            ? _states.Deserialize(serialized)
            : null;

    private void Stage(TaskData data)
        => _state.Value = _states.SerializeToArray(data);

    private void StageForTurn(TaskData data)
    {
        var previous = _state.Value is { Length: > 0 } serialized
            ? serialized.ToArray()
            : [];
        Stage(data);
        EnlistTurnRollback(() => _state.Value = previous);
    }

    private async Task SaveAsync(TaskData data)
    {
        Stage(data);
        await WriteStateAsync().ConfigureAwait(true);
    }
}
