namespace DigitalBrain.Poc.Runtime;

public sealed record CandidateInvocationScope(
    string OwnerId,
    CandidateFamilyId Family,
    string Revision,
    CandidateModuleIdentity ModuleIdentity,
    string InputDeliveryId)
{
    public static CandidateInvocationScope ForTest(
        string ownerId,
        CandidateFamilyId family,
        string revision) =>
        new(
            ownerId,
            family,
            revision,
            new CandidateModuleIdentity(
                new string('a', 64),
                new string('b', 64),
                new string('c', 64)),
            $"test-{Guid.NewGuid():N}");
}
