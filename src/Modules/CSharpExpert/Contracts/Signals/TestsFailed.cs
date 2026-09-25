using DigitalBrain.Contracts;
using DigitalBrain.Microsoft.DotNet;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.tests-failed")]
public sealed record TestsFailed(
    [property: Id(0)] string RunId,
    [property: Id(1)] int StepNumber,
    [property: Id(2)] IReadOnlyList<TestFailure> Failures) : Signal;
