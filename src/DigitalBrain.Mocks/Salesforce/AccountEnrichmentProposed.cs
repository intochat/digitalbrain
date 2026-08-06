namespace DigitalBrain.Mocks;

public sealed record AccountEnrichmentProposed(
    string? AccountId,
    string Domain,
    string FieldDiff,
    double Confidence) : Synapse;
