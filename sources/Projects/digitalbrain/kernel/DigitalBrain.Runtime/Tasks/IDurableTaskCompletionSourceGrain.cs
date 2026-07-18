using Orleans;

namespace DigitalBrain.Runtime.Tasks;

[Alias("IDurableTaskCompletionSourceGrain")]
public interface IDurableTaskCompletionSourceGrain : IGrainWithStringKey
{
    [Alias("TrySetResult")]
    ValueTask<bool> TrySetResult(string value);

    [Alias("TrySetException")]
    ValueTask<bool> TrySetException(string message);

    [Alias("TrySetCanceled")]
    ValueTask<bool> TrySetCanceled();

    [Alias("GetResult")]
    ValueTask<string> GetResult();

    [Alias("GetState")]
    ValueTask<DurableTaskCompletionSourceState> GetState();
}
