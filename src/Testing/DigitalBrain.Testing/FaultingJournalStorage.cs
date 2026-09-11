using System.Buffers;
using System.Collections.Concurrent;
using Orleans.Journaling;

namespace DigitalBrain.Testing;

public sealed class JournalFaultPlan
{
    private readonly ConcurrentDictionary<string, int> _writes = new(StringComparer.Ordinal);
    private int _nextWrite;
    private ReadHold? _readHold;
    private NumberedFault? _numberedFault;

    public Task ReadHeld => Volatile.Read(ref _readHold)?.Held.Task
        ?? throw new InvalidOperationException("No storage read hold was armed.");

    public void HoldNextRead() => Interlocked.Exchange(ref _readHold, new ReadHold())?.Release.TrySetResult();

    public void ReleaseRead() => Volatile.Read(ref _readHold)?.Release.TrySetResult();

    public void FailNextWrite() => Interlocked.Exchange(ref _nextWrite, (int)NextWriteFault.Refuse);

    public void CancelNextWrite() => Interlocked.Exchange(ref _nextWrite, (int)NextWriteFault.Cancel);

    public void FailWriteNumber(string journalId, int ordinal)
    {
        ArgumentException.ThrowIfNullOrEmpty(journalId);
        ArgumentOutOfRangeException.ThrowIfLessThan(ordinal, 1);
        Interlocked.Exchange(ref _numberedFault, new(journalId, ordinal));
    }

    public void Clear()
    {
        Interlocked.Exchange(ref _nextWrite, (int)NextWriteFault.None);
        Interlocked.Exchange(ref _numberedFault, null);
        _writes.Clear();
        Interlocked.Exchange(ref _readHold, null)?.Release.TrySetResult();
    }

    internal async Task BeforeReadAsync(CancellationToken cancellationToken)
    {
        var hold = Volatile.Read(ref _readHold);
        if (hold is not null && Interlocked.Exchange(ref hold.Claimed, 1) == 0)
        {
            hold.Held.TrySetResult();
            await hold.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal void BeforeWrite(string journalId)
    {
        var ordinal = _writes.AddOrUpdate(journalId, 1, static (_, count) => count + 1);
        var nextWrite = (NextWriteFault)Interlocked.Exchange(ref _nextWrite, (int)NextWriteFault.None);
        var numberedFault = Volatile.Read(ref _numberedFault);
        var failNumberedWrite = numberedFault is not null
            && string.Equals(journalId, numberedFault.JournalId, StringComparison.Ordinal)
            && ordinal == numberedFault.Ordinal
            && ReferenceEquals(Interlocked.CompareExchange(ref _numberedFault, null, numberedFault), numberedFault);
        if (nextWrite == NextWriteFault.Cancel)
        {
            throw new OperationCanceledException("faulting journal storage cancelled the write");
        }

        if (nextWrite == NextWriteFault.Refuse || failNumberedWrite)
        {
            throw new IOException("faulting journal storage refused the write");
        }
    }

    private sealed class ReadHold
    {
        internal readonly TaskCompletionSource Held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Claimed;
    }

    private sealed record NumberedFault(string JournalId, int Ordinal);

    private enum NextWriteFault
    {
        None,
        Refuse,
        Cancel,
    }
}

internal sealed class FaultingJournalStorageProvider(IJournalStorageProvider inner, JournalFaultPlan faults) : IJournalStorageProvider
{
    public IJournalStorage CreateStorage(JournalId id)
        => new FaultingJournalStorage(inner.CreateStorage(id), id.Value, faults);
}

internal sealed class FaultingJournalStorage(IJournalStorage inner, string journalId, JournalFaultPlan faults) : IJournalStorage
{
    public bool IsCompactionRequested => inner.IsCompactionRequested;

    public async ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken)
    {
        await faults.BeforeReadAsync(cancellationToken).ConfigureAwait(false);
        await inner.ReadAsync(consumer, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
    {
        faults.BeforeWrite(journalId);
        return inner.AppendAsync(value, cancellationToken);
    }

    public ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
    {
        faults.BeforeWrite(journalId);
        return inner.ReplaceAsync(value, cancellationToken);
    }

    public ValueTask DeleteAsync(CancellationToken cancellationToken) => inner.DeleteAsync(cancellationToken);
}
