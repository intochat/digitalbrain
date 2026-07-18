using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Dynamic;

[GenerateSerializer]
public sealed record PlanApproved([property: Id(1)] string PlanId,
    [property: Id(2)] IReadOnlyList<PlanItem> Items
) : Synapse;

[GenerateSerializer]
public sealed record PlanItemDue([property: Id(1)] string PlanId,
    [property: Id(2)] PlanItem Item
) : Synapse;
