using Brain.Contracts;

namespace Brain.Kernel;

public interface IReactiveStore
{
    IDictionary<string, CommandReceipt> Receipts { get; }
    IDictionary<string, byte> ProcessedEvents { get; }
    IDictionary<string, long> SourceSequences { get; }
    IList<OutboxIntent> Outbox { get; }
    IDictionary<string, string> Domain { get; }
    IDictionary<string, string> Flags { get; }
    IList<SanitizedFailure> Failures { get; }
    IDictionary<string, byte> RejectedCausation { get; }
    Task CommitAsync();
}

public sealed class InMemoryReactiveStore : IReactiveStore
{
    public bool FailNextCommit { get; set; }

    public IDictionary<string, CommandReceipt> Receipts { get; } = new Dictionary<string, CommandReceipt>(StringComparer.Ordinal);
    public IDictionary<string, byte> ProcessedEvents { get; } = new Dictionary<string, byte>(StringComparer.Ordinal);
    public IDictionary<string, long> SourceSequences { get; } = new Dictionary<string, long>(StringComparer.Ordinal);
    public IList<OutboxIntent> Outbox { get; } = new List<OutboxIntent>();
    public IDictionary<string, string> Domain { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public IDictionary<string, string> Flags { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public IList<SanitizedFailure> Failures { get; } = new List<SanitizedFailure>();
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
