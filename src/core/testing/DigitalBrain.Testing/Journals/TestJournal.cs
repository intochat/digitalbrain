using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "TestBrain owns journal lifetime and disposes asynchronously.")]
public sealed partial class TestJournal
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
            ISessionNeuron.ForOwner(subject.Owner).ToGrainId());
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
}
