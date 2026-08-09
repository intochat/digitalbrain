namespace DigitalBrain.Poc.Runtime;

internal sealed record CandidateModuleBinding(
    string OwnerId,
    string Family,
    string Revision,
    CandidateModuleIdentity Identity);
