using DigitalBrain.Contracts;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.build-passed")]
public sealed record BuildPassed(
    [property: Id(0)] string RunId,
    [property: Id(1)] int StepNumber,
    [property: Id(2)] int WarningCount) : Signal;
