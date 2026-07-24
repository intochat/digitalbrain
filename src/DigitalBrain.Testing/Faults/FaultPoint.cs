using Orleans.Runtime;

namespace DigitalBrain.Testing;

public abstract record FaultPoint;

public sealed record JournalCommitAfter(
    GrainId Grain,
    int CompletedWritesBeforeFailure,
    string Message) : FaultPoint;
