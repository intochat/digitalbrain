using DigitalBrain.Contracts;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.step-done")]
public sealed record StepDone(
    [property: Id(0)] string RunId,
    [property: Id(1)] int StepNumber) : Signal;
