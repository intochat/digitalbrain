using DigitalBrain.Contracts;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.tests-passed")]
public sealed record TestsPassed(
    [property: Id(0)] string RunId,
    [property: Id(1)] int StepNumber,
    [property: Id(2)] int Passed,
    [property: Id(3)] int Total) : Signal;
