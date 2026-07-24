using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "TestBrain exclusively owns journal lifetime and invokes the internal asynchronous cleanup before releasing the fixture lease.")]
public sealed class TestJournal
{
    private readonly FixtureCluster _cluster;
    private readonly BrainTestDiagnostics _diagnostics;
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

    public async Task<ObservedSynapse<TSynapse>> NextAsync<TSynapse>(
        CancellationToken cancellationToken = default)
        where TSynapse : Synapse
    {
        try
        {
            var observed =
                await NextCoreAsync<TSynapse>(cancellationToken);
            _diagnostics.RecordEvent(
                "journal.next",
                "succeeded",
                ("subject", _subject.ToString()),
                ("direction", _direction.ToString()),
                ("synapse.type", typeof(TSynapse).FullName ?? typeof(TSynapse).Name),
                ("sequence", observed.Sequence.ToString(CultureInfo.InvariantCulture)));
            return observed;
        }
        catch (Exception failure)
            when (failure is not BrainTestFailureException)
        {
            throw _diagnostics.CaptureFailure(
                "journal.next",
                failure);
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

                JournalObservation observation;

                try
                {
                    observation =
                        await observer.Observations.ReadAsync(cancellationToken);
                }
                catch (ChannelClosedException closed)
                    when (closed.InnerException is InvalidOperationException failure)
                {
                    throw ObservationFailure(failure);
                }
                catch (ChannelClosedException closed)
                    when (closed.InnerException is not null)
                {
                    ExceptionDispatchInfo.Capture(closed.InnerException).Throw();
                    throw;
                }

                if (Accept<TSynapse>(observer, observation) is { } accepted)
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

    public async Task<IReadOnlyList<ObservedSynapse<TSynapse>>> ReadAsync<TSynapse>(
        long afterSequence = 0,
        CancellationToken cancellationToken = default)
        where TSynapse : Synapse
    {
        try
        {
            ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);
            var observed = await ReadCoreAsync<TSynapse>(
                afterSequence,
                cancellationToken);
            _diagnostics.RecordEvent(
                "journal.read",
                "succeeded",
                ("subject", _subject.ToString()),
                ("direction", _direction.ToString()),
                ("synapse.type", typeof(TSynapse).FullName ?? typeof(TSynapse).Name),
                ("count", observed.Count.ToString(CultureInfo.InvariantCulture)));
            return observed;
        }
        catch (Exception failure)
            when (failure is not BrainTestFailureException)
        {
            throw _diagnostics.CaptureFailure(
                "journal.read",
                failure);
        }
    }

    private async Task<IReadOnlyList<ObservedSynapse<TSynapse>>> ReadCoreAsync<TSynapse>(
        long afterSequence,
        CancellationToken cancellationToken)
        where TSynapse : Synapse
    {
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

        await _nextGate.WaitAsync();
        _nextGate.Release();

        if (failures.Count > 0)
        {
            throw new AggregateException(
                $"Cleanup failed for the {_direction} journal of '{_subject}'.",
                failures);
        }
    }

    private SynapseDelivery? Accept<TSynapse>(
        TestJournalObserver observer,
        JournalObservation observation)
        where TSynapse : Synapse
    {
        if (observation.Read.ResetSnapshot is not null)
        {
            throw CompactionFailure(
                observation.RequestedCursor,
                observation.Read);
        }

        SynapseDelivery? matching = null;

        foreach (var delivery in observation.Read.Delta)
        {
            if (matching is null && delivery.Synapse is TSynapse)
            {
                matching = delivery;
                continue;
            }

            if (_pending.Count >= TestJournalObserver.EvidenceLimit)
            {
                var failure = PendingOverflowFailure(observation);
                observer.Complete(failure);
                throw failure;
            }

            _pending.Add(delivery);
        }

        return matching;
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

    private InvalidOperationException ObservationFailure(
        InvalidOperationException failure)
        => new(
            $"Journal evidence failed for subject '{_subject}', direction '{_direction}': {failure.Message}",
            failure);

    private InvalidOperationException PendingOverflowFailure(
        JournalObservation observation)
        => new(
            $"Journal evidence overflow for subject '{_subject}', direction '{_direction}': unmatched evidence exceeded limit {TestJournalObserver.EvidenceLimit} while processing the batch requested after cursor {observation.RequestedCursor}, with resume sequence {observation.Read.ResumeSequence}.");

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
