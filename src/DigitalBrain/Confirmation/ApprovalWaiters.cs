using System.Collections.Concurrent;

namespace DigitalBrain.Confirmation;

/// <summary>
/// In-memory asynchronous waiter coordination for confirmation requests awaiting external resolution.
/// </summary>
public sealed class ApprovalWaiters
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ConfirmationResult>> _waiters = new(StringComparer.Ordinal);

    public Task<ConfirmationResult> WaitFor(string planId, CancellationToken ct = default)
    {
        var tcs = _waiters.GetOrAdd(planId, _ => new TaskCompletionSource<ConfirmationResult>(TaskCreationOptions.RunContinuationsAsynchronously));
        if (ct.CanBeCanceled)
        {
            var reg = ct.Register(() =>
            {
                if (_waiters.TryRemove(KeyValuePair.Create(planId, tcs)))
                {
                    tcs.TrySetCanceled(ct);
                }
            });
            _ = tcs.Task.ContinueWith(_ => reg.Dispose(), TaskScheduler.Default);
        }
        return tcs.Task;
    }

    public void Complete(string planId, ConfirmationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (_waiters.TryRemove(planId, out var tcs))
        {
            tcs.TrySetResult(result);
        }
    }

    public void Fail(string planId, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (_waiters.TryRemove(planId, out var tcs))
        {
            tcs.TrySetException(error);
        }
    }

    public void Cancel(string planId)
    {
        if (_waiters.TryRemove(planId, out var tcs))
        {
            tcs.TrySetCanceled();
        }
    }
}
