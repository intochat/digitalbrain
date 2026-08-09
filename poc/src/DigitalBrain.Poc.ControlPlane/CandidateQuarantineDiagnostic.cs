using System.Text.Json.Serialization;

namespace DigitalBrain.Poc.ControlPlane;

public sealed record CandidateQuarantineDiagnostic(
    string CandidateId,
    string Stage,
    string Detail)
{
    [JsonIgnore]
    public string Path { get; init; } = string.Empty;
}
