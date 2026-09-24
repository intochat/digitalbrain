using DigitalBrain.Contracts;

namespace DigitalBrain.Apps.Signals;

[GenerateSerializer, Alias("apps.app-invocation-completed")]
public sealed record AppInvocationCompleted([property: Id(0)] AppInvocation Invocation) : Signal;
