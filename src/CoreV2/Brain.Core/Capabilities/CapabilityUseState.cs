using Brain.Abstractions.Identity;

namespace Brain.Core.Capabilities;

internal readonly record struct CapabilityUseKey(
    BrainActivityId Activity,
    CapabilityId Capability,
    CapabilityUseName Name);

internal interface ICapabilityUseSlot;

internal interface ICapabilityUseSlot<TResult> : ICapabilityUseSlot
    where TResult : class
{
    Task<TResult> Result { get; }

    void Complete(TResult result);

    void Fail(Exception error);
}

internal sealed class CapabilityUseSlot<TResult> : ICapabilityUseSlot<TResult>
    where TResult : class
{
    private readonly TaskCompletionSource<TResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<TResult> Result => _result.Task;

    public void Complete(TResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _result.TrySetResult(result);
    }

    public void Fail(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        _result.TrySetException(error);
    }
}

internal sealed class CapabilityUseState
{
    private readonly Lock _gate = new();
    private readonly Dictionary<CapabilityUseKey, ICapabilityUseSlot> _uses = [];

    internal Task<TResult> UseAsync<TResult>(
        CapabilityUseKey key,
        Func<Task<TResult>> invoke)
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(invoke);
        ICapabilityUseSlot<TResult> slot;
        var ownsInvocation = false;
        lock (_gate)
        {
            if (_uses.TryGetValue(key, out var recorded))
            {
                slot = recorded as ICapabilityUseSlot<TResult>
                    ?? throw new CapabilityTypeMismatchException(
                        $"Capability use '{key.Name}' was already recorded with another result type.");
            }
            else
            {
                slot = new CapabilityUseSlot<TResult>();
                _uses.Add(key, slot);
                ownsInvocation = true;
            }
        }

        return ownsInvocation
            ? InvokeAndRecordAsync(key, slot, invoke)
            : slot.Result;
    }

    private async Task<TResult> InvokeAndRecordAsync<TResult>(
        CapabilityUseKey key,
        ICapabilityUseSlot<TResult> slot,
        Func<Task<TResult>> invoke)
        where TResult : class
    {
        try
        {
            var result = await invoke().ConfigureAwait(false);
            slot.Complete(result);
            return result;
        }
        catch (Exception error)
        {
            slot.Fail(error);
            lock (_gate)
            {
                if (_uses.TryGetValue(key, out var current) && ReferenceEquals(current, slot))
                {
                    _uses.Remove(key);
                }
            }

            throw;
        }
    }
}
