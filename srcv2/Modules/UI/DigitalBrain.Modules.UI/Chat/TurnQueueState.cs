namespace DigitalBrain.UI;

[GenerateSerializer]
internal sealed record TurnQueueState(
    [property: Id(0)] List<Guid> PendingTurnIds,
    [property: Id(1)] Guid? ActiveTurnId,
    [property: Id(2)] string? ActiveExecutionName);