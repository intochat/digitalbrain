namespace DigitalBrain.Mocks;

public sealed record ProposeAccountEnrichment(
    string? AccountId,
    string Domain,
    string FieldDiff,
    double Confidence) : Synapse;
