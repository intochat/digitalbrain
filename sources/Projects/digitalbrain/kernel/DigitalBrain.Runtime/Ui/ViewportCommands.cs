using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Ui;

[GenerateSerializer]
public sealed record MoveCameraCommand(
    [property: Id(1)] float TargetX,
    [property: Id(2)] float TargetY,
    [property: Id(3)] float TargetZ,
    [property: Id(4)] float DampingRatio = 0.72f,
    [property: Id(5)] float NaturalFreq = 14.0f
) : Synapse;

[GenerateSerializer]
public sealed record FocusNeuronCommand(
    [property: Id(1)] string NeuronId,
    [property: Id(2)] float ZoomDepth = 2.0f
) : Synapse;
