using Brain.Contracts;

namespace Brain.Kernel;

[GenerateSerializer, Alias("brain.outbox-intent.v1")]
public sealed record OutboxIntent(
    [property: Id(0)] Guid IntentId,
    [property: Id(1)] Guid EventId,
    [property: Id(2)] Guid CommandId,
    [property: Id(3)] Guid CausationId,
    [property: Id(4)] Guid CorrelationId,
    [property: Id(5)] OrganizationId OrganizationId,
    [property: Id(6)] PrincipalId PrincipalId,
    [property: Id(7)] SpaceId SpaceId,
    [property: Id(8)] NeuronAddress Source,
    [property: Id(9)] long SourceSequence,
    [property: Id(10)] int CausalDepth,
    [property: Id(11)] DateTimeOffset OccurredAt,
    [property: Id(12)] string StreamNamespace,
    [property: Id(13)] string PayloadType,
    [property: Id(14)] string Payload,
    [property: Id(15)] int AttemptCount)
{
    public static OutboxIntent Create(SynapseMetadata metadata, string streamNamespace, string payload) =>
        new(
            IntentId: Guid.NewGuid(),
            EventId: Guid.NewGuid(),
            CommandId: metadata.CommandId,
            CausationId: metadata.EventId == Guid.Empty ? metadata.CommandId : metadata.EventId,
            CorrelationId: metadata.CorrelationId,
            OrganizationId: metadata.OrganizationId,
            PrincipalId: metadata.PrincipalId,
            SpaceId: metadata.SpaceId,
            Source: metadata.Source,
            SourceSequence: metadata.SourceSequence,
            CausalDepth: metadata.CausalDepth + 1,
            OccurredAt: DateTimeOffset.UtcNow,
            StreamNamespace: streamNamespace,
            PayloadType: payload.GetType().FullName ?? "string",
            Payload: payload,
            AttemptCount: 0);

    public OutboxIntent WithAttempt(int attemptCount) => this with { AttemptCount = attemptCount };

    public EventSynapse<string> ToEventSynapse(NeuronAddress publisher) =>
        new(
            new SynapseMetadata(
                CommandId: CommandId,
                EventId: EventId,
                CausationId: CausationId,
                CorrelationId: CorrelationId,
                OrganizationId: OrganizationId,
                PrincipalId: PrincipalId,
                SpaceId: SpaceId,
                Source: publisher,
                SourceSequence: SourceSequence,
                CausalDepth: CausalDepth,
                OccurredAt: OccurredAt),
            Payload);
}
