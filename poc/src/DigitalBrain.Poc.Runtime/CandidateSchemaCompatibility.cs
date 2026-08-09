namespace DigitalBrain.Poc.Runtime;

public sealed class CandidateSchemaCompatibility
{
    private readonly RunStore _store;

    public CandidateSchemaCompatibility(PocDataRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _store = new RunStore(root);
    }

    public Task<bool> HasRetainedFamilyJournalAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return _store.ReadAsync(
            document => document.CandidateModuleBindings.Any(binding =>
                string.Equals(binding.OwnerId, principal.OwnerId, StringComparison.Ordinal) &&
                string.Equals(binding.Family, family.Value, StringComparison.Ordinal)),
            cancellationToken);
    }
}
