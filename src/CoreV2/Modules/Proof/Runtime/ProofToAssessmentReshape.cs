using Brain.Abstractions.Reshapes;
using Brain.Modules.Proof.Contracts;

namespace Brain.Modules.Proof;

public sealed class ProofToAssessmentReshape : IReshape<ProofProduced, ProofAssessed>
{
    public ProofAssessed Transform(ProofProduced source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new ProofAssessed(source.Value, "assessed/" + source.Value);
    }
}
