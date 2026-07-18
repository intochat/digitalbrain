using Orleans.Journaling;

namespace DigitalBrain.Runtime.Tasks;

[GrainType("DurableTaskCompletionSourceGrain")]
public sealed class DurableTaskCompletionSourceGrain(
    [FromKeyedServices("state")] IDurableValue<DurableTaskCompletionSourceState> state)
    : DurableGrain, IDurableTaskCompletionSourceGrain
{
    private readonly List<TaskCompletionSource<string>> _pending = new();

    public async ValueTask<bool> TrySetResult(string value)
    {
        var current = state.Value;
        if (current != null && current.IsCompleted) return false;

        state.Value = new DurableTaskCompletionSourceState(
            IsCompleted: true,
            IsFaulted: false,
            IsCanceled: false,
            Result: value,
            ErrorMessage: null
        );
        await WriteStateAsync();

        lock (_pending)
        {
            foreach (var tcs in _pending)
            {
                tcs.TrySetResult(value);
            }
            _pending.Clear();
        }

        return true;
    }

    public async ValueTask<bool> TrySetException(string message)
    {
        var current = state.Value;
        if (current != null && current.IsCompleted) return false;

        state.Value = new DurableTaskCompletionSourceState(
            IsCompleted: true,
            IsFaulted: true,
            IsCanceled: false,
            Result: default,
            ErrorMessage: message
        );
        await WriteStateAsync();

        lock (_pending)
        {
            foreach (var tcs in _pending)
            {
                tcs.TrySetException(new InvalidOperationException(message));
            }
            _pending.Clear();
        }

        return true;
    }

    public async ValueTask<bool> TrySetCanceled()
    {
        var current = state.Value;
        if (current != null && current.IsCompleted) return false;

        state.Value = new DurableTaskCompletionSourceState(
            IsCompleted: true,
            IsFaulted: false,
            IsCanceled: true,
            Result: default,
            ErrorMessage: "Canceled"
        );
        await WriteStateAsync();

        lock (_pending)
        {
            foreach (var tcs in _pending)
            {
                tcs.TrySetCanceled();
            }
            _pending.Clear();
        }

        return true;
    }

    public async ValueTask<string> GetResult()
    {
        var current = state.Value;
        if (current != null && current.IsCompleted)
        {
            if (current.IsFaulted)
            {
                throw new InvalidOperationException(current.ErrorMessage ?? "Task faulted");
            }
            if (current.IsCanceled)
            {
                throw new TaskCanceledException("Task canceled");
            }
            return current.Result!;
        }

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending)
        {
            _pending.Add(tcs);
        }

        return await tcs.Task;
    }

    public ValueTask<DurableTaskCompletionSourceState> GetState()
    {
        return ValueTask.FromResult(state.Value ?? new DurableTaskCompletionSourceState(false, false, false, default, null));
    }
}
