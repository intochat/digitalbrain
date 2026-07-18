using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ui.Visuals;

[GenerateSerializer]
public sealed record MaterialSpecResolved([property: Id(1)] string ClientId,
    [property: Id(2)] MaterialPlan Plan
) : Synapse;
