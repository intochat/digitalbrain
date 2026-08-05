using System.Buffers;
using Orleans.Journaling;

namespace DigitalBrain.Testing;

internal sealed class RecordingJournalStorageProvider(IJournalStorageProvider inner) : IJournalStorageProvider
{
    private readonly Dictionary<JournalId, JournalFaultState> faults = [];
    private readonly Lock gate = new();

    public IJournalStorage CreateStorage(JournalId journalId)
        => new RecordingJournalStorage(this, journalId, inner.CreateStorage(journalId));

    internal JournalFaultRegistration ArmFault(
        NeuronId target, string message, int allowCommitsBeforeFault, bool stickyUntilDisarm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentOutOfRangeException.ThrowIfNegative(allowCommitsBeforeFault);

        lock (gate)
        {
            var journalId = JournalIdOf(target);
            if (faults.ContainsKey(journalId))
            {
                throw new InvalidOperationException(
                    $"A journal commit fault is already armed for neuron '{target}'.");
            }

            var state = new JournalFaultState(target, message, allowCommitsBeforeFault, stickyUntilDisarm);
            faults.Add(journalId, state);
            return new JournalFaultRegistration(target, message, state.Consumed.Task, state);
        }
    }

    internal bool DisarmFault(JournalFaultRegistration registration)
    {
        lock (gate)
        {
            var journalId = JournalIdOf(registration.Target);
            if (!faults.TryGetValue(journalId, out var state) || !ReferenceEquals(state, registration.Token))
            {
                return false;
            }

            faults.Remove(journalId);
            return true;
        }
    }

    internal IReadOnlyList<string> UnconsumedFaults()
    {
        lock (gate)
        {
            return [.. faults.Values
                .Where(state => !state.Consumed.Task.IsCompleted)
                .Select(state => $"{state.Target}: {state.Message}")
                .Order(StringComparer.Ordinal)];
        }
    }

    private static JournalId JournalIdOf(NeuronId target)
        => JournalId.FromGrainId(GrainId.Create(target.Kind, target.Name));

    private void BeforeWrite(JournalId journalId)
    {
        lock (gate)
        {
            if (!faults.TryGetValue(journalId, out var fault))
            {
                return;
            }

            if (fault.RemainingAllowedCommits > 0)
            {
                fault.RemainingAllowedCommits--;
                return;
            }

            fault.Consumed.TrySetResult();
            // One-shot faults remove themselves so later commits succeed; sticky faults keep
            // failing until disarmed, so redelivery cannot leap past a faulted turn.
            if (!fault.StickyUntilDisarm)
            {
                faults.Remove(journalId);
            }

            throw new InvalidOperationException(fault.Message);
        }
    }

    private sealed class JournalFaultState(
        NeuronId target, string message, int allowCommitsBeforeFault, bool stickyUntilDisarm)
    {
        internal TaskCompletionSource Consumed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal NeuronId Target { get; } = target;

        internal string Message { get; } = message;

        internal int RemainingAllowedCommits { get; set; } = allowCommitsBeforeFault;

        internal bool StickyUntilDisarm { get; } = stickyUntilDisarm;
    }

    private sealed class RecordingJournalStorage(
        RecordingJournalStorageProvider recorder, JournalId journalId, IJournalStorage inner) : IJournalStorage
    {
        public bool IsCompactionRequested => inner.IsCompactionRequested;

        public async ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
        {
            recorder.BeforeWrite(journalId);
            await inner.AppendAsync(value, cancellationToken);
        }

        public async ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
        {
            recorder.BeforeWrite(journalId);
            await inner.ReplaceAsync(value, cancellationToken);
        }

        public ValueTask<bool> CreateIfNotExistsAsync(
            IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken)
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
