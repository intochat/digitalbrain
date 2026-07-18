using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Dynamic;

[GenerateSerializer]
public sealed record PlanItem(
    [property: Id(0)] DayOfWeek Day,
    [property: Id(1)] string Time,
    [property: Id(2)] string Title,
    [property: Id(3)] string Owner,
    [property: Id(4)] string? Note);

[GenerateSerializer]
public sealed record WeeklyPlanProposed([property: Id(1)] string PlanId,
    [property: Id(2)] string ChosenDirection,
    [property: Id(3)] string Rationale,
    [property: Id(4)] IReadOnlyList<PlanItem> Items,
    [property: Id(5)] IReadOnlyList<string> Participants,
    [property: Id(6)] IReadOnlyList<string> ConversationTranscript
) : Synapse;
