namespace Brain.Contracts;

public sealed record NeuronReply<T>(T Value, long Revision, string? EffectKey);

[GenerateSerializer, Alias("brain.invocation.v2")]
public sealed record NeuronInvocation(
    [property: Id(0)] string Contract,
    [property: Id(1)] string InputJson,
    [property: Id(2)] string CommandId,
    [property: Id(3)] string CallerKey,
    [property: Id(4)] long? ExpectedRevision = null);

[GenerateSerializer, Alias("brain.receipt.v2")]
public sealed record NeuronReceipt(
    [property: Id(0)] string CommandId,
    [property: Id(1)] long Revision,
    [property: Id(2)] string Status,
    [property: Id(3)] string OutputJson,
    [property: Id(4)] string? EffectKey = null);

[GenerateSerializer, Alias("brain.event.v2")]
public sealed record NeuronEvent(
    [property: Id(0)] long Revision,
    [property: Id(1)] string Kind,
    [property: Id(2)] string PayloadJson,
    [property: Id(3)] string CommandId,
    [property: Id(4)] DateTimeOffset OccurredAt);

[GenerateSerializer, Alias("brain.event-page.v2")]
public sealed record NeuronEventPage([property: Id(0)] NeuronEvent[] Events, [property: Id(1)] long NextRevision);

[GenerateSerializer, Alias("brain.description.v2")]
public sealed record NeuronDescription(
    [property: Id(0)] string Kind,
    [property: Id(1)] long Revision,
    [property: Id(2)] string[] Contracts);

[GenerateSerializer, Alias("brain.snapshot.v2")]
public sealed record NeuronSnapshot([property: Id(0)] long Revision, [property: Id(1)] string StateJson);

[GenerateSerializer, Alias("brain.approved-effect-proof.v2")]
public sealed record ApprovedEffectProof(
    [property: Id(0)] string EffectKey,
    [property: Id(1)] long EffectRevision,
    [property: Id(2)] string PayloadDigest,
    [property: Id(3)] string DecisionCommandId);
