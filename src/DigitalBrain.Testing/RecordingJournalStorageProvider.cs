using System.Buffers;
using System.Collections.Concurrent;
using DigitalBrain.Abstractions;
using Orleans.Journaling;
using Orleans.Runtime;

namespace DigitalBrain.Testing;

internal sealed class RecordingJournalStorageProvider(IJournalStorageProvider inner)
    : IJournalStorageProvider
{
    private readonly ConcurrentDictionary<JournalId, long> _completedWrites = new();
    private readonly Dictionary<NeuronId, JournalFaultState> _failures = [];
    private readonly Dictionary<JournalId, NeuronId> _faultTargets = [];
    private readonly object _failureLock = new();

    public IJournalStorage CreateStorage(JournalId journalId)
        => new RecordingJournalStorage(this, journalId, inner.CreateStorage(journalId));

    internal long CompletedWrites(GrainId grain)
        => _completedWrites.GetValueOrDefault(JournalId.FromGrainId(grain));

    internal JournalFaultRegistration ArmFault(
        NeuronId target,
        int completedWritesBeforeFailure,
        string message)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(completedWritesBeforeFailure);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        lock (_failureLock)
        {
            if (_failures.ContainsKey(target))
            {
                throw new InvalidOperationException(
                    $"A journal commit fault is already armed for neuron '{target}'.");
            }

            var state = new JournalFaultState(
                completedWritesBeforeFailure,
                message);
            _failures.Add(target, state);
            _faultTargets.Add(
                JournalId.FromGrainId(target.ToGrainId()),
                target);
            return new(target, message, state.Consumed.Task, state);
        }
    }

    internal bool DisarmFault(JournalFaultRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        lock (_failureLock)
        {
            if (!_failures.TryGetValue(registration.Target, out var state)
                || !ReferenceEquals(state, registration.Token))
            {
                return false;
            }

            RemoveFault(registration.Target);
            return true;
        }
    }

    private void BeforeWrite(JournalId journalId)
    {
        lock (_failureLock)
        {
            if (!_faultTargets.TryGetValue(journalId, out var target)
                || !_failures.TryGetValue(target, out var failure))
            {
                return;
            }

            if (failure.RemainingWrites > 0)
            {
                failure.RemainingWrites--;
                return;
            }

            RemoveFault(target);
            failure.Consumed.TrySetResult();
            throw new InvalidOperationException(failure.Message);
        }
    }

    private void AfterWrite(JournalId journalId)
        => _completedWrites.AddOrUpdate(journalId, 1, static (_, count) => count + 1);

    private void RemoveFault(NeuronId target)
    {
        _failures.Remove(target);
        _faultTargets.Remove(JournalId.FromGrainId(target.ToGrainId()));
    }

    private sealed class JournalFaultState(
        int remainingWrites,
        string message)
    {
        internal TaskCompletionSource Consumed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal string Message { get; } = message;

        internal int RemainingWrites { get; set; } = remainingWrites;
    }

    private sealed class RecordingJournalStorage(
        RecordingJournalStorageProvider recorder,
        JournalId journalId,
        IJournalStorage inner) : IJournalStorage
    {
        public bool IsCompactionRequested => inner.IsCompactionRequested;

        public async ValueTask AppendAsync(
            ReadOnlySequence<byte> value,
            CancellationToken cancellationToken)
        {
            recorder.BeforeWrite(journalId);
            await inner.AppendAsync(value, cancellationToken);
            recorder.AfterWrite(journalId);
        }

        public async ValueTask ReplaceAsync(
            ReadOnlySequence<byte> value,
            CancellationToken cancellationToken)
        {
            recorder.BeforeWrite(journalId);
            await inner.ReplaceAsync(value, cancellationToken);
            recorder.AfterWrite(journalId);
        }

        public ValueTask<bool> CreateIfNotExistsAsync(
            IReadOnlyDictionary<string, string>? metadata,
            CancellationToken cancellationToken)
            => inner.CreateIfNotExistsAsync(metadata, cancellationToken);

        public ValueTask DeleteAsync(CancellationToken cancellationToken)
            => inner.DeleteAsync(cancellationToken);

        public ValueTask<IJournalMetadata?> GetMetadataAsync(CancellationToken cancellationToken)
            => inner.GetMetadataAsync(cancellationToken);

        public ValueTask ReadAsync(
            IJournalStorageConsumer consumer,
            CancellationToken cancellationToken)
            => inner.ReadAsync(consumer, cancellationToken);

        public ValueTask<IJournalMetadata?> UpdateMetadataAsync(
            IReadOnlyDictionary<string, string>? metadata,
            IEnumerable<string>? tagsToRemove,
            string? eTag,
            CancellationToken cancellationToken)
            => inner.UpdateMetadataAsync(metadata, tagsToRemove, eTag, cancellationToken);
    }
}

internal sealed record JournalFaultRegistration(
    NeuronId Target,
    string Message,
    Task Consumed,
    object Token);
