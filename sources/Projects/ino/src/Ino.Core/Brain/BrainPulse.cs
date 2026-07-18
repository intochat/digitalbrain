namespace Ino.Core.Brain;

/// <summary>
/// A single grain-call observation. Emitted by <c>BrainTraceFilter</c> on
/// every <see cref="Orleans.Runtime.IIncomingGrainCallContext"/> invocation
/// onto the <c>ino-brain</c> stream. Consumers: the Flutter brain screen
/// (logged in C.3, rendered in C.4) and any future inspector tab.
///
/// <see cref="InoInstanceId"/> is the per-user session id sourced from
/// <c>RequestContext.Get("ino.sessionId")</c>; the brain UI hashes it to a
/// stable hue so concurrent ino-instances render as distinct trails (spec
/// §4.4).
/// </summary>
[GenerateSerializer]
public sealed record BrainPulse(
    [property: Id(0)] string TraceParent,
    [property: Id(1)] string InoInstanceId,
    [property: Id(2)] string UserId,
    [property: Id(3)] string FromGrain,
    [property: Id(4)] string ToGrain,
    [property: Id(5)] string MethodName,
    [property: Id(6)] long DurationMs,
    [property: Id(7)] BrainPulseStatus Status,
    [property: Id(8)] long TimestampUnixMs,
    [property: Id(9)] string PayloadJson);

[GenerateSerializer]
public enum BrainPulseStatus
{
    Ok = 0,
    Failed = 1,
}
