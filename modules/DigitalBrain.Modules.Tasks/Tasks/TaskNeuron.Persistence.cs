using DigitalBrain.Tasks.Persistence;

namespace DigitalBrain.Tasks;

internal sealed partial class TaskNeuron
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

    private async Task SaveAsync(TaskData data)
    {
        Stage(data);
        await WriteStateAsync();
    }
}
