using Brain.Abstractions.Events;

namespace Brain.Modules.Proof.Contracts;

public sealed record ProofProduced(string Value) : IDomainEvent;
public sealed record ProofAssessed(string Value, string Assessment) : IDomainEvent;
