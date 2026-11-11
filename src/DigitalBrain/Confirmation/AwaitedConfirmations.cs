namespace DigitalBrain.Confirmation;

/// <summary>
/// Adapter implementing <see cref="IConfirmations"/> over <see cref="IConfirmationRequests"/> and <see cref="IConfirmationInbox"/>
/// for callers expecting a blocking asynchronous task.
/// </summary>
public sealed class AwaitedConfirmations(
    IConfirmationRequests requests,
    IConfirmationInbox inbox,
    ConfirmationPolicy policy,
    ApprovalWaiters? waiters = null) : IConfirmations
{
    private readonly ApprovalWaiters _waiters = waiters ?? new ApprovalWaiters();

    public ConfirmationPolicy Policy => policy;

    public async Task<ConfirmationResult> Confirm(ConfirmationPlan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ct.ThrowIfCancellationRequested();

        var immediate = await requests.Request(plan).WaitAsync(ct).ConfigureAwait(false);
        if (immediate is not null)
        {
            return immediate;
        }

        try
        {
            var outcome = await requests.Outcome(plan.Id).WaitAsync(ct).ConfigureAwait(false);
            if (outcome is not null)
            {
                return outcome;
            }

            var waitTask = _waiters.WaitFor(plan.Id, ct);
            while (!waitTask.IsCompleted)
            {
                var check = await requests.Outcome(plan.Id).WaitAsync(ct).ConfigureAwait(false);
                if (check is not null)
                {
                    _waiters.Complete(plan.Id, check);
                    return check;
                }
                await Task.WhenAny(waitTask, Task.Delay(50, ct)).ConfigureAwait(false);
            }
            return await waitTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _waiters.Cancel(plan.Id);
            try { await requests.Withdraw(plan.Id).ConfigureAwait(false); } catch { }
            throw;
        }
    }

    public Task Resolve(string planId, bool approve, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ct.ThrowIfCancellationRequested();
        return inbox.Resolve(planId, approve);
    }
}
