using System.Buffers;
using System.Collections.Concurrent;
using Orleans.Journaling;
using Orleans.Runtime;

namespace DigitalBrain.Testing;

internal sealed class RecordingJournalStorageProvider(IJournalStorageProvider inner)
    : IJournalStorageProvider
{
    private readonly ConcurrentDictionary<JournalId, long> _completedWrites = new();
    private readonly Dictionary<JournalId, InjectedFailure> _failures = [];
    private readonly object _failureLock = new();

    public IJournalStorage CreateStorage(JournalId journalId)
        => new RecordingJournalStorage(this, journalId, inner.CreateStorage(journalId));

    internal long CompletedWrites(GrainId grain)
        => _completedWrites.GetValueOrDefault(JournalId.FromGrainId(grain));

    internal void FailWriteAfter(
        GrainId grain,
        int completedWritesBeforeFailure,
        string message)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(completedWritesBeforeFailure);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        lock (_failureLock)
        {
            _failures[JournalId.FromGrainId(grain)] = new(completedWritesBeforeFailure, message);
        }
    }

    internal void ClearFailure(GrainId grain)
    {
        lock (_failureLock)
        {
            _failures.Remove(JournalId.FromGrainId(grain));
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

            if (failure.CompletedWritesBeforeFailure > 0)
            {
                _failures[journalId] = failure with
                {
                    CompletedWritesBeforeFailure = failure.CompletedWritesBeforeFailure - 1,
                };

                return;
            }

            _failures.Remove(journalId);
            throw new InvalidOperationException(failure.Message);
        }
    }

    private void AfterWrite(JournalId journalId)
        => _completedWrites.AddOrUpdate(journalId, 1, static (_, count) => count + 1);

    private sealed record InjectedFailure(int CompletedWritesBeforeFailure, string Message);

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
