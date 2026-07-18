namespace DigitalBrain.Protocol.Domain.Events;

[GenerateSerializer]
public sealed record RuleMatched([property: Id(0)] string BundleIdValue, [property: Id(1)] int RuleIndex, [property: Id(2)] string Trigger, [property: Id(3)] Guid CorrelationId) : Synapse;

[GenerateSerializer]
public sealed record RuleFault([property: Id(0)] string BundleIdValue, [property: Id(1)] int RuleIndex, [property: Id(2)] string Message, [property: Id(3)] Guid? IncomingId = null) : Synapse;

[GenerateSerializer]
public sealed record RuleSuspended([property: Id(0)] string BundleIdValue, [property: Id(1)] int RuleIndex) : Synapse;
