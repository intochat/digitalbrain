using DigitalBrain.Contracts;

namespace DigitalBrain.Apps.Signals;

// Live notification only: a behavior that was not subscribed drains IApp.Pending when it starts.
[GenerateSerializer, Alias("apps.app-invoked")]
public sealed record AppInvoked([property: Id(0)] Guid InvocationId, [property: Id(1)] string Operation, [property: Id(2)] string Input) : Signal;
