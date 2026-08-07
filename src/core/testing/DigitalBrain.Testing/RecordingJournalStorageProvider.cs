using System.Buffers;
using DigitalBrain.Abstractions;
using Orleans.Journaling;

namespace DigitalBrain.Testing;

internal sealed class RecordingJournalStorageProvider(IJournalStorageProvider inner)
    : IJournalStorageProvider
{
    private readonly Dictionary<JournalId, JournalFaultState> _failures = [];
    private readonly object _failureLock = new();

    public IJournalStorage CreateStorage(JournalId journalId)
        => new RecordingJournalStorage(this, journalId, inner.CreateStorage(journalId));

    internal JournalFaultRegistration ArmFault(
        NeuronId target,
        string message,
        int allowCommitsBeforeFault = 0,
        bool stickyUntilDisarm = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentOutOfRangeException.ThrowIfNegative(allowCommitsBeforeFault);

        lock (_failureLock)
        {
            var journalId = JournalId.FromGrainId(target.ToGrainId());
            if (_failures.ContainsKey(journalId))
            {
                throw new InvalidOperationException(
                    $"A journal commit fault is already armed for neuron '{target}'.");
            }

            var state = new JournalFaultState(message, allowCommitsBeforeFault, stickyUntilDisarm);
            _failures.Add(journalId, state);
            return new(target, message, state.Consumed.Task, state);
        }
    }

    internal bool DisarmFault(JournalFaultRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        lock (_failureLock)
        {
            var journalId = JournalId.FromGrainId(registration.Target.ToGrainId());
            if (!_failures.TryGetValue(journalId, out var state)
                || !ReferenceEquals(state, registration.Token))
            {
                return false;
            }

            _failures.Remove(journalId);
            return true;
        }
    }

    private void BeforeWrite(JournalId journalId)
    {
        lock (_failureLock)
        {
            if (!_failures.TryGetValue(journalId, out var failure))
            {
                return;
            }

            if (failure.RemainingAllowedCommits > 0)
            {
                failure.RemainingAllowedCommits--;
                return;
            }

            failure.Consumed.TrySetResult();
            // One-shot faults remove themselves so later commits succeed. Sticky faults keep
            // failing until DisarmFault so outbox redelivery cannot leap past a faulted turn.
            if (!failure.StickyUntilDisarm)
            {
                _failures.Remove(journalId);
            }

            throw new InvalidOperationException(failure.Message);
        }
    }

    private sealed class JournalFaultState(string message, int allowCommitsBeforeFault, bool stickyUntilDisarm)
    {
        internal TaskCompletionSource Consumed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal string Message { get; } = message;

        internal int RemainingAllowedCommits { get; set; } = allowCommitsBeforeFault;

        internal bool StickyUntilDisarm { get; } = stickyUntilDisarm;
    }

    private sealed class RecordingJournalStorage(
        RecordingJournalStorageProvider recorder,
        JournalId journalId,
        IJournalStorage inner) : IJournalStorage
    {
        public bool IsCompactionRequested => inner.IsCompactionRequested;

        public async ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
        {
            recorder.BeforeWrite(journalId);
            await inner.AppendAsync(value, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
        {
            recorder.BeforeWrite(journalId);
            await inner.ReplaceAsync(value, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask<bool> CreateIfNotExistsAsync(
            IReadOnlyDictionary<string, string>? metadata,
            CancellationToken cancellationToken)
            => inner.CreateIfNotExistsAsync(metadata, cancellationToken);

        public ValueTask DeleteAsync(CancellationToken cancellationToken)
            => inner.DeleteAsync(cancellationToken);

        public ValueTask<IJournalMetadata?> GetMetadataAsync(CancellationToken cancellationToken)
            => inner.GetMetadataAsync(cancellationToken);

        public ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken)
            => inner.ReadAsync(consumer, cancellationToken);

        public ValueTask<IJournalMetadata?> UpdateMetadataAsync(
            IReadOnlyDictionary<string, string>? metadata,
            IEnumerable<string>? tagsToRemove,
            string? eTag,
            CancellationToken cancellationToken)
            => inner.UpdateMetadataAsync(metadata, tagsToRemove, eTag, cancellationToken);
    }
}

internal sealed record JournalFaultRegistration(NeuronId Target, string Message, Task Consumed, object Token);
