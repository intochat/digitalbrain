using Brain.Contracts;
using Brain.Kernel;

namespace Brain.Tests.Kernel;

public sealed class InMemoryReactiveStore<TOutboxEvent> : IReactiveStore<TOutboxEvent>
{
    public bool FailNextCommit { get; set; }

    public IDictionary<string, CommandReceipt> Receipts { get; } = new Dictionary<string, CommandReceipt>(StringComparer.Ordinal);
    public IDictionary<string, byte> ProcessedEvents { get; } = new Dictionary<string, byte>(StringComparer.Ordinal);
    public IDictionary<string, long> SourceSequences { get; } = new Dictionary<string, long>(StringComparer.Ordinal);
    public IList<OutboxIntent<TOutboxEvent>> Outbox { get; } = new List<OutboxIntent<TOutboxEvent>>();
    public IDictionary<string, string> Domain { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public IDictionary<string, string> Flags { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public IList<SanitizedFailure> Failures { get; } = new List<SanitizedFailure>();
    public IDictionary<string, byte> AcceptedCausation { get; } = new Dictionary<string, byte>(StringComparer.Ordinal);
    public IDictionary<string, byte> RejectedCausation { get; } = new Dictionary<string, byte>(StringComparer.Ordinal);

    public Task CommitAsync()
    {
        if (FailNextCommit)
        {
            FailNextCommit = false;
            throw new BrainException(BrainErrors.JournalCommitFailed, "journal write failed");
        }

        return Task.CompletedTask;
    }
}
