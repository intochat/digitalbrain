namespace Brain.Contracts;

[GenerateSerializer, Alias("brain.synapse-metadata.v1")]
public sealed record SynapseMetadata(
    [property: Id(0)] Guid CommandId,
    [property: Id(1)] Guid EventId,
    [property: Id(2)] Guid CausationId,
    [property: Id(3)] Guid CorrelationId,
    [property: Id(4)] OrganizationId OrganizationId,
    [property: Id(5)] PrincipalId PrincipalId,
    [property: Id(6)] SpaceId SpaceId,
    [property: Id(7)] NeuronAddress Source,
    [property: Id(8)] long SourceSequence,
    [property: Id(9)] int CausalDepth,
    [property: Id(10)] DateTimeOffset OccurredAt);

[GenerateSerializer, Alias("brain.command-synapse.v1")]
public sealed record CommandSynapse<T>(
    [property: Id(0)] SynapseMetadata Metadata,
    [property: Id(1)] T Payload);

[GenerateSerializer, Alias("brain.event-synapse.v1")]
public sealed record EventSynapse<T>(
    [property: Id(0)] SynapseMetadata Metadata,
    [property: Id(1)] T Payload);

[GenerateSerializer, Alias("brain.command-receipt-status.v1")]
public enum CommandReceiptStatus
{
    Accepted = 0,
    Duplicate = 1,
    Rejected = 2,
    Failed = 3,
}

[GenerateSerializer, Alias("brain.command-receipt.v1")]
public sealed record CommandReceipt(
    [property: Id(0)] Guid CommandId,
    [property: Id(1)] CommandReceiptStatus Status,
    [property: Id(2)] long Revision,
    [property: Id(3)] string? FailureCode,
    [property: Id(4)] string? FailureMessage);

[GenerateSerializer, Alias("brain.start-discussion.v1")]
public sealed record StartDiscussion(
    [property: Id(0)] string Topic,
    [property: Id(1)] string GptKey,
    [property: Id(2)] string GrokKey);
