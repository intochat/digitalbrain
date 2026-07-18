using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Dynamic;

public enum CreatorStage
{
    Planning,
    GherkinValid,
    StepsCompiled,
    ImplCompiled,
    TestsRunning,
    TestsGreen,
    Promoted,
    Retrying,
    Failed,
}

[GenerateSerializer]
public sealed record CreatorProgress([property: Id(1)] string NeuronId,
    [property: Id(2)] int Attempt,
    [property: Id(3)] CreatorStage Stage,
    [property: Id(4)] string? Detail
) : Synapse;
