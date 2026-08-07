using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

public sealed partial class TestJournal
{
    private async Task<ObservedSynapse<TSynapse>> NextWrappedAsync<TSynapse>(CancellationToken cancellationToken)
        where TSynapse : Synapse
    {
        try
        {
            return await NextCoreAsync<TSynapse>(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not BrainTestFailureException)
        {
            throw _diagnostics.CaptureFailure("journal.next", failure);
        }
    }

    private async Task<ObservedSynapse<TSynapse>> NextCoreAsync<TSynapse>(CancellationToken cancellationToken)
        where TSynapse : Synapse
    {
        await _nextGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var observer = await EnsureWatchingAsync(cancellationToken).ConfigureAwait(false);

            while (true)
            {
                if (TakePending<TSynapse>() is { } pending)
                {
                    return Observe((TSynapse)pending.Synapse, pending);
                }

                JournalRead batch;
                try
                {
                    batch = await observer.Observations.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (ChannelClosedException closed)
                    when (closed.InnerException is not null)
                {
                    ExceptionDispatchInfo.Capture(closed.InnerException).Throw();
                    throw;
                }

                if (Accept<TSynapse>(observer, batch) is { } accepted)
                {
                    return Observe((TSynapse)accepted.Synapse, accepted);
                }
            }
        }
        finally
        {
            _nextGate.Release();
        }
    }

    private SynapseDelivery? Accept<TSynapse>(TestJournalObserver observer, JournalRead batch)
        where TSynapse : Synapse
    {
        if (batch.ResetSnapshot is not null)
        {
            throw new InvalidOperationException(
                $"Journal compaction for '{_subject}' {_direction}.");
        }

        SynapseDelivery? matching = null;
        foreach (var delivery in batch.Delta)
        {
            if (matching is null && delivery.Synapse is TSynapse)
            {
                matching = delivery;
                continue;
            }

            if (_pending.Count >= TestJournalObserver.EvidenceLimit)
            {
                var failure = new InvalidOperationException(
                    $"Journal evidence overflow for '{_subject}' {_direction}.");
                observer.Complete(failure);
                throw failure;
            }

            _pending.Add(delivery);
        }

        return matching;
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Failed watch setup cleans up registration and reference best-effort.")]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Observer ownership transfers on success.")]
    private async Task<TestJournalObserver> EnsureWatchingAsync(CancellationToken cancellationToken)
    {
        await _setupGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_observer is not null)
            {
                return _observer;
            }

            var observer = new TestJournalObserver(_direction);
            IJournalObserver? reference = null;
            try
            {
                reference = _cluster.Client.CreateObjectReference<IJournalObserver>(observer);
                await _session.WatchNeuron(_subject, _direction, afterSequence: 0, reference).ConfigureAwait(false);
                _observer = observer;
                _reference = reference;
                _watching = true;
                return observer;
            }
            catch
            {
                observer.Complete();
                if (reference is not null)
                {
                    try
                    {
                        await _session.UnwatchNeuron(_subject, reference).ConfigureAwait(false);
                    }
                    catch
                    {
                    }

                    try
                    {
                        _cluster.Client.DeleteObjectReference<IJournalObserver>(reference);
                    }
                    catch
                    {
                    }
                }

                try
                {
                    await observer.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                }

                throw;
            }
        }
        finally
        {
            _setupGate.Release();
        }
    }

    private void RetireNextTask(Task task)
    {
        lock (_nextTasksGate)
        {
            _outstandingNextTasks.Remove(task);
        }
    }

    private ObservedSynapse<TSynapse> Observe<TSynapse>(TSynapse synapse, SynapseDelivery delivery)
        where TSynapse : Synapse
        => new(
            synapse,
            delivery.SynapseId,
            delivery.Sequence,
            delivery.Timestamp,
            delivery.CorrelationId,
            delivery.CausationId,
            delivery.Caller,
            _direction);

    private SynapseDelivery? TakePending<TSynapse>()
        where TSynapse : Synapse
    {
        var index = _pending.FindIndex(delivery => delivery.Synapse is TSynapse);
        if (index < 0)
        {
            return null;
        }

        var delivery = _pending[index];
        _pending.RemoveAt(index);
        return delivery;
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
