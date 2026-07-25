using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "TestBrain owns journal lifetime and disposes asynchronously.")]
public sealed class TestJournal
{
    private readonly FixtureCluster _cluster;
    private readonly BrainTestDiagnostics _diagnostics;
    private readonly JournalKind _direction;
    private readonly SemaphoreSlim _nextGate = new(1, 1);
    private readonly Lock _nextTasksGate = new();
    private readonly HashSet<Task> _outstandingNextTasks = [];
    private readonly List<SynapseDelivery> _pending = [];
    private readonly ISessionNeuron _session;
    private readonly SemaphoreSlim _setupGate = new(1, 1);
    private readonly NeuronId _subject;

    private int _disposed;
    private TestJournalObserver? _observer;
    private IJournalObserver? _reference;
    private bool _watching;

    internal TestJournal(
        FixtureCluster cluster,
        NeuronId subject,
        JournalKind direction,
        BrainTestDiagnostics diagnostics)
    {
        _cluster = cluster;
        _subject = subject;
        _direction = direction;
        _diagnostics = diagnostics;
        _session = cluster.Client.GetGrain<ISessionNeuron>(
            new NeuronId(
                ISessionNeuron.GrainTypeName,
                subject.Owner,
                "session").ToGrainId());
    }

    public Task<ObservedSynapse<TSynapse>> NextAsync<TSynapse>(
        CancellationToken cancellationToken = default)
        where TSynapse : Synapse
    {
        lock (_nextTasksGate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return Task.FromException<ObservedSynapse<TSynapse>>(
                    _diagnostics.CaptureFailure(
                        "journal.next",
                        new ObjectDisposedException(nameof(TestJournal))));
            }

            var task = NextWrappedAsync<TSynapse>(cancellationToken);
            _outstandingNextTasks.Add(task);
            _ = task.ContinueWith(
                static (completed, state) =>
                    ((TestJournal)state!).RetireNextTask(completed),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return task;
        }
    }

    public async Task<IReadOnlyList<ObservedSynapse<TSynapse>>> ReadAsync<TSynapse>(
        long afterSequence = 0,
        CancellationToken cancellationToken = default)
        where TSynapse : Synapse
    {
        try
        {
            ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);
            ThrowIfDisposed();
            var read = await _session
                .ReadNeuronJournal(_subject, _direction, afterSequence)
                .WaitAsync(cancellationToken);

            if (read.ResetSnapshot is not null)
            {
                throw new InvalidOperationException(
                    $"Journal compaction for '{_subject}' {_direction} after {afterSequence}.");
            }

            return
            [
                .. read.Delta
                    .Where(delivery => delivery.Synapse is TSynapse)
                    .Select(delivery => Observe(
                        (TSynapse)delivery.Synapse,
                        delivery)),
            ];
        }
        catch (Exception failure) when (failure is not BrainTestFailureException)
        {
            throw _diagnostics.CaptureFailure("journal.read", failure);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Teardown attempts every cleanup step and aggregates failures.")]
    internal async ValueTask DisposeAsync()
    {
        lock (_nextTasksGate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            Volatile.Write(ref _disposed, 1);
        }

        TestJournalObserver? observer;
        IJournalObserver? reference;
        var watching = false;

        await _setupGate.WaitAsync();
        try
        {
            observer = _observer;
            reference = _reference;
            watching = _watching;
            _observer = null;
            _reference = null;
            _watching = false;
            observer?.Complete(new ObjectDisposedException(nameof(TestJournal)));
        }
        finally
        {
            _setupGate.Release();
        }

        List<Exception> failures = [];

        if (observer is not null && reference is not null)
        {
            if (watching)
            {
                try
                {
                    await _session.UnwatchNeuron(_subject, reference);
                }
                catch (Exception failure)
                {
                    failures.Add(failure);
                }
            }

            try
            {
                _cluster.Client.DeleteObjectReference<IJournalObserver>(reference);
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }

            try
            {
                await observer.DisposeAsync();
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
        }

        Task[] outstanding;
        lock (_nextTasksGate)
        {
            outstanding = [.. _outstandingNextTasks];
        }

        foreach (var task in outstanding)
        {
            try
            {
                await task;
            }
            catch
            {
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException(
                $"Cleanup failed for the {_direction} journal of '{_subject}'.",
                failures);
        }
    }

    private async Task<ObservedSynapse<TSynapse>> NextWrappedAsync<TSynapse>(
        CancellationToken cancellationToken)
        where TSynapse : Synapse
    {
        try
        {
            return await NextCoreAsync<TSynapse>(cancellationToken);
        }
        catch (Exception failure) when (failure is not BrainTestFailureException)
        {
            throw _diagnostics.CaptureFailure("journal.next", failure);
        }
    }

    private async Task<ObservedSynapse<TSynapse>> NextCoreAsync<TSynapse>(
        CancellationToken cancellationToken)
        where TSynapse : Synapse
    {
        await _nextGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            var observer = await EnsureWatchingAsync(cancellationToken);

            while (true)
            {
                if (TakePending<TSynapse>() is { } pending)
                {
                    return Observe((TSynapse)pending.Synapse, pending);
                }

                JournalRead batch;
                try
                {
                    batch = await observer.Observations.ReadAsync(cancellationToken);
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

    private SynapseDelivery? Accept<TSynapse>(
        TestJournalObserver observer,
        JournalRead batch)
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
    private async Task<TestJournalObserver> EnsureWatchingAsync(
        CancellationToken cancellationToken)
    {
        await _setupGate.WaitAsync(cancellationToken);
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
                await _session.WatchNeuron(
                    _subject,
                    _direction,
                    afterSequence: 0,
                    reference);
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
                        await _session.UnwatchNeuron(_subject, reference);
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
                    await observer.DisposeAsync();
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

    private static ObservedSynapse<TSynapse> Observe<TSynapse>(
        TSynapse synapse,
        SynapseDelivery delivery)
        where TSynapse : Synapse
        => new(synapse, delivery.SynapseId);

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
        => ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
}
