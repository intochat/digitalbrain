using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Custom;

[GenerateSerializer]
public sealed record EmailSummaryCompleted(
    [property: Id(1)] string Success
) : Synapse;
