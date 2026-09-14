using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.open-workspace")]
public sealed record OpenWorkspace(
    CommandId Id,
    [property: Id(0)] string SolutionPath,
    long? ExpectedVersion = null) : Command(Id, ExpectedVersion);
