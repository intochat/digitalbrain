using Orleans;

namespace Ino.Kernel.Contracts;

public interface IProposalLog : IGrainWithStringKey
{
    /// <summary>
    /// Records a new pending proposal entry. Called from
    /// <see cref="INeuronRegistry.StashDraftAsync"/> when a draft is
    /// stashed for approval. Idempotent on <see cref="ProposalEntry.ProposalId"/>.
    /// </summary>
    Task RecordPendingAsync(ProposalEntry entry);

    Task<IReadOnlyList<ProposalEntry>> ListAsync(ProposalStatus? filter, int skip, int take);
    Task<ProposalEntry?> GetAsync(string proposalId);
    Task RecordDecisionAsync(string proposalId, ProposalStatus decision, string decidedBy);
}
