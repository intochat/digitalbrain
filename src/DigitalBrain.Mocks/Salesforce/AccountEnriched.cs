namespace DigitalBrain.Mocks;

public sealed record AccountEnriched(
    string? AccountId,
    string Domain,
    string FieldDiff) : Synapse;
