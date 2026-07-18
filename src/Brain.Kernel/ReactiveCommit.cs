using Brain.Contracts;

namespace Brain.Kernel;

public sealed record ReactiveCommit(
    string DomainState,
    long UiRevision,
    IReadOnlyList<OutboxIntent> Outbox);

public delegate Task CommitReactionAsync(ReactiveCommit commit);

public delegate Task<CommandReceiptStatus> CommandHandlerAsync<T>(T payload, CommitReactionAsync commit);

public delegate Task EventHandlerAsync<T>(T payload, CommitReactionAsync commit);
