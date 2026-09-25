namespace DigitalBrain.Core;

public enum ActivityKind { CallStarted, CallArrived, CallCompleted, CallFailed, SignalPublished }

public sealed record ActivityEvent(
    string ScopeId,
    long Sequence,
    Guid Id,
    Guid OperationId,
    string? CorrelationId,
    DateTimeOffset At,
    ActivityKind Kind,
    string? SourceId,
    string? TargetId,
    string Type,
    string Status,
    double? DurationMs,
    string? FailureCode);

public sealed record ActivitySnapshot(
    IReadOnlyList<ActivityEvent> Events,
    long NextSequence,
    bool Gap,
    DateTimeOffset ObservedAt);

public sealed record ActivityUpdate(ActivityEvent? Event, bool Gap);
