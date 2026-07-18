using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Placement;
using Ino.Kernel.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;

namespace Ino.Kernel;

/// <summary>
/// Kernel-pinned grain that tracks the full lifecycle of every
/// <see cref="L1Proposal"/>: Pending when stashed by
/// <see cref="INeuronRegistry.StashDraftAsync"/>, Approved or Rejected
/// when the user decides via the inspector. State is in-memory (v0.1);
/// durable persistence is tracked under issue #22.
///
/// Reacts to:
/// <list type="bullet">
///   <item><see cref="ProposalDecided"/> — flips an existing entry to Approved or Rejected.</item>
/// </list>
/// The initial Pending state is recorded via <see cref="RecordPendingAsync"/>,
/// called directly from <see cref="INeuronRegistry.StashDraftAsync"/>
/// (in the genesis silo) as a cross-silo grain call. This avoids having
/// both <c>ProposalLog</c> and <c>CreatorNeuron</c> implement
/// <c>IReactsTo&lt;L1Proposal&gt;</c>, which would cause Orleans broadcast
/// ambiguity under interface-only resolution.
/// </summary>
[PinToSilo("kernel")]
public sealed class ProposalLog(ILogger<ProposalLog>? logger = null)
    : Grain, IProposalLog,
        IReactsTo<ProposalDecided>
{
    private readonly Dictionary<string, ProposalEntry> _entries = new(StringComparer.Ordinal);
    private readonly ILogger _log = (ILogger?)logger ?? NullLogger.Instance;

    public Task RecordPendingAsync(ProposalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_entries.ContainsKey(entry.ProposalId)) return Task.CompletedTask;
        _entries[entry.ProposalId] = entry;
        _log.LogInformation("ProposalLog: recorded pending {ProposalId}", entry.ProposalId);
        return Task.CompletedTask;
    }

    public Task ReactAsync(ProposalDecided decided, NeuronContext ctx, CancellationToken ct)
    {
        if (!_entries.TryGetValue(decided.ProposalId, out var existing)) return Task.CompletedTask;
        _entries[decided.ProposalId] = existing with
        {
            Status = decided.Decision,
            DecidedAt = decided.DecidedAt,
            DecidedBy = decided.DecidedBy,
        };
        _log.LogInformation(
            "ProposalLog: {Decision} {ProposalId} by {DecidedBy}",
            decided.Decision, decided.ProposalId, decided.DecidedBy);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProposalEntry>> ListAsync(ProposalStatus? filter, int skip, int take)
    {
        IEnumerable<ProposalEntry> q = _entries.Values.OrderByDescending(e => e.ProposedAt);
        if (filter is { } f) q = q.Where(e => e.Status == f);
        IReadOnlyList<ProposalEntry> result = q.Skip(skip).Take(take).ToArray();
        return Task.FromResult(result);
    }

    public Task<ProposalEntry?> GetAsync(string proposalId) =>
        Task.FromResult(_entries.GetValueOrDefault(proposalId));

    public Task RecordDecisionAsync(string proposalId, ProposalStatus decision, string decidedBy)
    {
        if (!_entries.TryGetValue(proposalId, out var existing))
            throw new InvalidOperationException($"Unknown proposal {proposalId}");
        _entries[proposalId] = existing with
        {
            Status = decision,
            DecidedAt = DateTimeOffset.UtcNow,
            DecidedBy = decidedBy,
        };
        return Task.CompletedTask;
    }
}
