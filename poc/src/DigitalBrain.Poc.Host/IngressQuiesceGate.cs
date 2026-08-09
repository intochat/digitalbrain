using DigitalBrain.Poc.Abstractions;

namespace DigitalBrain.Poc.Host;

public sealed class IngressQuiesceGate
{
    private readonly object _sync = new();
    private bool _open = true;
    private int _registered;
    private int _queued;
    private TaskCompletionSource _drained = CompletedDrain();

    public IngressAdmissionLease Acquire(
        Func<Synapse, CancellationToken, Task> enqueue)
    {
        ArgumentNullException.ThrowIfNull(enqueue);
        lock (_sync)
        {
            if (!_open)
            {
                throw new HostQuiescingException();
            }

            if (_registered == 0 && _queued == 0)
            {
                _drained = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            _registered++;
            return new IngressAdmissionLease(this, enqueue);
        }
    }

    public void Close()
    {
        lock (_sync)
        {
            _open = false;
            CompleteDrainIfEmpty();
        }
    }

    public void Reopen()
    {
        lock (_sync)
        {
            _open = true;
        }
    }

    public Task WaitForDrainAsync(CancellationToken cancellationToken = default)
    {
        Task drain;
        lock (_sync)
        {
            CompleteDrainIfEmpty();
            drain = _drained.Task;
        }

        return drain.WaitAsync(cancellationToken);
    }

    internal void TransferRegistrationToQueuedTurn()
    {
        lock (_sync)
        {
            if (_registered <= 0)
            {
                throw new InvalidOperationException("The ingress admission lease is not registered.");
            }

            _registered--;
            _queued++;
        }
    }

    internal void CompleteQueuedTurn()
    {
        lock (_sync)
        {
            if (_queued <= 0)
            {
                throw new InvalidOperationException("No queued ingress turn is registered.");
            }

            _queued--;
            CompleteDrainIfEmpty();
        }
    }

    internal void ReleaseUnusedRegistration()
    {
        lock (_sync)
        {
            if (_registered <= 0)
            {
                throw new InvalidOperationException("The ingress admission lease is not registered.");
            }

            _registered--;
            CompleteDrainIfEmpty();
        }
    }

    private void CompleteDrainIfEmpty()
    {
        if (_registered == 0 && _queued == 0)
        {
            _drained.TrySetResult();
        }
    }

    private static TaskCompletionSource CompletedDrain()
    {
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        completed.SetResult();
        return completed;
    }
}
