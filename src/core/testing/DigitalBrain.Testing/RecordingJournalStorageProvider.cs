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

    internal JournalFaultRegistration ArmFault(NeuronId target, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        lock (_failureLock)
        {
            var journalId = JournalId.FromGrainId(target.ToGrainId());
            if (_failures.ContainsKey(journalId))
            {
                throw new InvalidOperationException(
                    $"A journal commit fault is already armed for neuron '{target}'.");
            }

            var state = new JournalFaultState(message);
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
            if (!_failures.Remove(journalId, out var failure))
            {
                return;
            }

            failure.Consumed.TrySetResult();
            throw new InvalidOperationException(failure.Message);
        }
    }

    private sealed class JournalFaultState(string message)
    {
        internal TaskCompletionSource Consumed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal string Message { get; } = message;
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
            await inner.AppendAsync(value, cancellationToken);
        }

        public async ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
        {
            recorder.BeforeWrite(journalId);
            await inner.ReplaceAsync(value, cancellationToken);
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
