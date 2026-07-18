using Brain.Contracts;

namespace Brain.Kernel;

public sealed record ReactiveCommit<TOutboxEvent>(
    string DomainState,
    long UiRevision,
    IReadOnlyList<OutboxIntent<TOutboxEvent>> Outbox);

public delegate Task CommitReactionAsync<TOutboxEvent>(ReactiveCommit<TOutboxEvent> commit);

public delegate Task<CommandReceiptStatus> CommandHandlerAsync<TCommand, TOutboxEvent>(
    TCommand payload,
    CommitReactionAsync<TOutboxEvent> commit);

public delegate Task EventHandlerAsync<TEvent, TOutboxEvent>(
    TEvent payload,
    CommitReactionAsync<TOutboxEvent> commit);
