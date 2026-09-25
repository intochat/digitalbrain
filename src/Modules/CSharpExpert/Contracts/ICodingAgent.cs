using DigitalBrain.Contracts;
using DigitalBrain.Microsoft.Roslyn;

namespace DigitalBrain.CSharpExpert;

[Alias("csharp-expert.coding-agent")]
public interface ICodingAgent : INeuron
{
    Task<string> Ask(string prompt, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EditRequest>> ProposeEdits(EditProposal proposal, CancellationToken cancellationToken = default);
}

[GenerateSerializer, Alias("csharp-expert.edit-proposal")]
public sealed record EditProposal(
    [property: Id(0)] string RunId,
    [property: Id(1)] int StepNumber,
    [property: Id(2)] string StepTitle,
    [property: Id(3)] string StepDetail,
    [property: Id(4)] IReadOnlyList<string> Files,
    [property: Id(5)] string ProjectMap,
    [property: Id(6)] string? Failure);
