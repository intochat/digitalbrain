using Brain.Contracts;

namespace Brain.Kernel;

[GenerateSerializer, Alias("brain.outbox-intent`1")]
public sealed record OutboxIntent<T>(
    [property: Id(0)] Guid IntentId,
    [property: Id(1)] string StreamNamespace,
    [property: Id(2)] Guid StreamId,
    [property: Id(3)] EventSynapse<T> Event,
    [property: Id(4)] int AttemptCount)
{
    public static OutboxIntent<T> Create(string streamNamespace, Guid streamId, EventSynapse<T> @event) =>
        new(
            IntentId: Guid.NewGuid(),
            StreamNamespace: streamNamespace,
            StreamId: streamId,
            Event: @event,
            AttemptCount: 0);

    public OutboxIntent<T> WithAttempt(int attemptCount) => this with { AttemptCount = attemptCount };
}
