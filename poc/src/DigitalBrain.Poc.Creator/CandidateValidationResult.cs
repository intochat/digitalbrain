namespace DigitalBrain.Poc.Creator;

public sealed record CandidateValidationResult(
    CandidatePolicyError Error,
    string Detail)
{
    public bool IsValid => Error == CandidatePolicyError.None;

    internal static CandidateValidationResult Valid { get; } = new(
        CandidatePolicyError.None,
        string.Empty);

    internal static CandidateValidationResult Reject(CandidatePolicyError error, string detail) =>
        new(error, detail);
}
