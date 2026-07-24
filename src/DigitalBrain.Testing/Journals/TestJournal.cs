using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.ExceptionServices;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "TestBrain exclusively owns journal lifetime and invokes the internal asynchronous cleanup before releasing the fixture lease.")]
public sealed class TestJournal
{
    private readonly FixtureCluster _cluster;
    private readonly JournalKind _direction;
    private readonly SemaphoreSlim _nextGate = new(1, 1);
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
        JournalKind direction)
    {
        _cluster = cluster;
        _subject = subject;
        _direction = direction;
        _session = cluster.Client.GetGrain<ISessionNeuron>(
            new NeuronId(
                ISessionNeuron.GrainTypeName,
                subject.Owner,
                "session").ToGrainId());
    }

    public async Task<ObservedSynapse<TSynapse>> NextAsync<TSynapse>(
        CancellationToken cancellationToken = default)
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

                var observation =
                    await observer.Observations.ReadAsync(cancellationToken);
                Accept(observation);
            }
        }
        finally
        {
            _nextGate.Release();
        }
    }

    public async Task<IReadOnlyList<ObservedSynapse<TSynapse>>> ReadAsync<TSynapse>(
        long afterSequence = 0,
        CancellationToken cancellationToken = default)
        where TSynapse : Synapse
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);
        ThrowIfDisposed();

        var read = await _session
            .ReadNeuronJournal(_subject, _direction, afterSequence)
            .WaitAsync(cancellationToken);

        if (read.ResetSnapshot is not null)
        {
            throw CompactionFailure(afterSequence, read);
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

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Journal teardown must attempt unwatch, object-reference deletion, and observer disposal; all failures are retained in an aggregate.")]
    internal async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        TestJournalObserver? observer;
        IJournalObserver? reference;
        bool watching;

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
                _cluster.Client.DeleteObjectReference<IJournalObserver>(
                    reference);
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

        if (failures.Count > 0)
        {
            throw new AggregateException(
                $"Cleanup failed for the {_direction} journal of '{_subject}'.",
                failures);
        }
    }

    private void Accept(JournalObservation observation)
    {
        if (observation.Read.ResetSnapshot is not null)
        {
            throw CompactionFailure(
                observation.RequestedCursor,
                observation.Read);
        }

        _pending.AddRange(observation.Read.Delta);
    }

    private InvalidOperationException CompactionFailure(
        long requestedCursor,
        JournalRead read)
    {
        var snapshot = read.ResetSnapshot
            ?? throw new InvalidOperationException(
                "A journal compaction failure requires a reset snapshot.");
        var dropped = Math.Max(
            0,
            snapshot.TotalRecorded - snapshot.RetainedCount);
        var tallies = string.Join(
            ", ",
            snapshot.Tallies
                .OrderBy(tally => tally.SynapseType, StringComparer.Ordinal)
                .Select(tally => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{tally.SynapseType}={tally.Recorded}")));

        return new InvalidOperationException(string.Create(
            CultureInfo.InvariantCulture,
            $"Journal compaction for subject '{_subject}', direction '{_direction}', requested cursor {requestedCursor}, reset resume sequence {read.ResumeSequence}: retained={snapshot.RetainedCount}, dropped={dropped}, tallies=[{tallies}]."));
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A failed watch can leave a server registration and local object reference; both cleanup steps are attempted and preserved with the setup failure.")]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Observer ownership transfers to TestJournal after a successful watch; every failed setup path disposes it in CleanupFailedSetupAsync.")]
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
                reference =
                    _cluster.Client.CreateObjectReference<IJournalObserver>(
                        observer);
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
            catch (Exception setupFailure)
            {
                var cleanupFailures =
                    await CleanupFailedSetupAsync(observer, reference);

                if (cleanupFailures.Count > 0)
                {
                    throw new AggregateException(
                        $"Setup and cleanup failed for the {_direction} journal of '{_subject}'.",
                        [setupFailure, .. cleanupFailures]);
                }

                ExceptionDispatchInfo.Capture(setupFailure).Throw();
                throw;
            }
        }
        finally
        {
            _setupGate.Release();
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Partial journal setup must attempt every applicable cleanup step and report all failures.")]
    private async Task<IReadOnlyList<Exception>> CleanupFailedSetupAsync(
        TestJournalObserver observer,
        IJournalObserver? reference)
    {
        List<Exception> failures = [];
        observer.Complete();

        if (reference is not null)
        {
            try
            {
                await _session.UnwatchNeuron(_subject, reference);
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }

            try
            {
                _cluster.Client.DeleteObjectReference<IJournalObserver>(
                    reference);
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
        }

        try
        {
            await observer.DisposeAsync();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }

        return failures;
    }

    private ObservedSynapse<TSynapse> Observe<TSynapse>(
        TSynapse synapse,
        SynapseDelivery delivery)
        where TSynapse : Synapse
        => new(
            synapse,
            _subject,
            delivery.Caller,
            _direction,
            delivery.Sequence,
            delivery.Timestamp,
            delivery.CorrelationId,
            delivery.SynapseId);

    private SynapseDelivery? TakePending<TSynapse>()
        where TSynapse : Synapse
    {
        var index =
            _pending.FindIndex(delivery => delivery.Synapse is TSynapse);
        if (index < 0)
        {
            return null;
        }

        var delivery = _pending[index];
        _pending.RemoveAt(index);
        return delivery;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
    }
}
