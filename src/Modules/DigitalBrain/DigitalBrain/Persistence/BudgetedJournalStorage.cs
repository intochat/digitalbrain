using System.Buffers;
using Orleans.Journaling;

namespace DigitalBrain.Core;

internal sealed class BudgetedJournalStorage(IJournalStorage inner, TimeSpan budget) : IJournalStorage
{
    public bool IsCompactionRequested => inner.IsCompactionRequested;

    public async ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operation.CancelAfter(budget);
        await inner.ReadAsync(consumer, operation.Token).ConfigureAwait(false);
    }

    public async ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operation.CancelAfter(budget);
        await inner.AppendAsync(value, operation.Token).ConfigureAwait(false);
    }

    public async ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operation.CancelAfter(budget);
        await inner.ReplaceAsync(value, operation.Token).ConfigureAwait(false);
    }

    public ValueTask DeleteAsync(CancellationToken cancellationToken) => inner.DeleteAsync(cancellationToken);
}
