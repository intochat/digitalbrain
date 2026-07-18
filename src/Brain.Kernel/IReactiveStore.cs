using Brain.Contracts;

namespace Brain.Kernel;

public interface IReactiveStore<TOutboxEvent>
{
    IDictionary<string, CommandReceipt> Receipts { get; }
    IDictionary<string, byte> ProcessedEvents { get; }
    IDictionary<string, long> SourceSequences { get; }
    IList<OutboxIntent<TOutboxEvent>> Outbox { get; }
    IDictionary<string, string> Domain { get; }
    IDictionary<string, string> Flags { get; }
    IList<SanitizedFailure> Failures { get; }
    IDictionary<string, byte> AcceptedCausation { get; }
    IDictionary<string, byte> RejectedCausation { get; }
    Task CommitAsync();
}
