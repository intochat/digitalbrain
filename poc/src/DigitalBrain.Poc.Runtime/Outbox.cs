using DigitalBrain.Poc.Charting.Contracts;

namespace DigitalBrain.Poc.Runtime;

public sealed class Outbox
{
    private readonly RunStore _store;

    public Outbox(PocDataRoot root)
    {
        _store = new RunStore(root);
    }

    public Task<IReadOnlyList<CommittedOutboxEntry>> ReadCommittedAsync(
        CancellationToken cancellationToken = default) =>
        _store.ReadAsync<IReadOnlyList<CommittedOutboxEntry>>(
            document => document.Outbox
                .Select(entry => new CommittedOutboxEntry(
                    entry.DeliveryId,
                    entry.ReceiptId,
                    entry.OutputOrdinal,
                    entry.Kind))
                .ToArray(),
            cancellationToken);

    public Task<IReadOnlyList<CommittedOutboxEntry>> ReadPendingAsync(
        CancellationToken cancellationToken = default) =>
        _store.ReadAsync<IReadOnlyList<CommittedOutboxEntry>>(
            document => document.Outbox
                .Where(entry => !entry.Delivered)
                .Select(entry => new CommittedOutboxEntry(
                    entry.DeliveryId,
                    entry.ReceiptId,
                    entry.OutputOrdinal,
                    entry.Kind))
                .ToArray(),
            cancellationToken);

    public Task<IReadOnlyList<CommittedOutboxEntry>> PendingTargetingCandidateRevisionAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        string revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        var normalizedRevision = revision.ToLowerInvariant();
        return _store.ReadAsync<IReadOnlyList<CommittedOutboxEntry>>(
            document => document.Outbox
                .Where(entry =>
                    !entry.Delivered &&
                    string.Equals(entry.OwnerId, principal.OwnerId, StringComparison.Ordinal) &&
                    string.Equals(entry.CandidateFamily, family.Value, StringComparison.Ordinal) &&
                    (string.Equals(entry.TargetRevision, normalizedRevision, StringComparison.Ordinal) ||
                        string.Equals(
                            entry.ProducingRevision,
                            normalizedRevision,
                            StringComparison.Ordinal)))
                .Select(entry => new CommittedOutboxEntry(
                    entry.DeliveryId,
                    entry.ReceiptId,
                    entry.OutputOrdinal,
                    entry.Kind))
                .ToArray(),
            cancellationToken);
    }

    public Task<IReadOnlyList<string>> ReadLogicalJournalKindsAsync(
        CancellationToken cancellationToken = default) =>
        _store.ReadAsync<IReadOnlyList<string>>(
            document => ProjectLogicalKinds(document, null, null),
            cancellationToken);

    public Task<IReadOnlyList<string>> ReadLogicalJournalKindsForReceiptAsync(
        string ownerId,
        string receiptId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptId);
        return _store.ReadAsync<IReadOnlyList<string>>(
            document => ProjectLogicalKinds(document, ownerId, receiptId),
            cancellationToken);
    }

    public Task<int> ReadGeneratedAcceptedCountAsync(
        string ownerId,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        return _store.ReadAsync(
            document => document.States.TryGetValue(
                $"state|{ownerId}|{family.Value}|DigitalBrain.Poc.Candidate.{family.Value}.ElonPostRuleNeuron",
                out var state)
                    ? state.TryGetProperty("AcceptedCount", out var count)
                        ? count.GetInt32()
                        : throw new InvalidDataException(
                            "Generated Elon rule state is missing AcceptedCount.")
                    : 0,
            cancellationToken);
    }

    internal Task<PendingTrustedTargetOutboxEnvelope?> ReadLastTrustedTargetAsync(
        string ownerId,
        CancellationToken cancellationToken = default) =>
        _store.ReadAsync(
            document => document.Outbox
                .Where(entry =>
                    string.Equals(entry.OwnerId, ownerId, StringComparison.Ordinal) &&
                    entry.TargetRevision is null &&
                    !string.IsNullOrWhiteSpace(entry.TargetScope))
                .Select(ToTrustedTarget)
                .LastOrDefault(),
            cancellationToken);

    private static IReadOnlyList<string> ProjectLogicalKinds(
        RunDocument document,
        string? ownerId,
        string? rootReceiptId)
    {
        HashSet<string>? causalReceipts = null;
        if (rootReceiptId is not null)
        {
            var key = ownerId + "\n" + rootReceiptId;
            causalReceipts = document.TrustedInputDeliveries.TryGetValue(key, out var roots)
                ? roots.ToHashSet(StringComparer.Ordinal)
                : [];
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var entry in document.Outbox.Where(entry =>
                    causalReceipts.Contains(entry.ReceiptId)))
                {
                    changed |= causalReceipts.Add(entry.DeliveryId);
                }
            }
        }

        return document.Journal
            .Where(entry => causalReceipts is null || causalReceipts.Contains(entry.ReceiptId))
            .Where(entry =>
                entry.Direction != "in" ||
                !document.Outbox.Any(outbox =>
                    IsModeledTrustedAddChartPointDelivery(outbox, entry)))
            .Select(entry => entry.Kind)
            .ToArray();
    }

    private static bool IsModeledTrustedAddChartPointDelivery(
        OutboxEntry outbox,
        JournalEntry journal) =>
        string.Equals(outbox.DeliveryId, journal.ReceiptId, StringComparison.Ordinal) &&
        string.Equals(outbox.Kind, nameof(AddChartPoint), StringComparison.Ordinal) &&
        string.Equals(journal.Kind, nameof(AddChartPoint), StringComparison.Ordinal) &&
        string.Equals(
            outbox.ContractAlias,
            ContractAlias.For(typeof(AddChartPoint)),
            StringComparison.Ordinal) &&
        string.Equals(outbox.PayloadFormat, "json", StringComparison.Ordinal) &&
        outbox.TargetRevision is null &&
        outbox.TargetModuleIdentity is null &&
        string.IsNullOrEmpty(outbox.TargetNeuronType) &&
        !string.IsNullOrWhiteSpace(outbox.OwnerId) &&
        !string.IsNullOrWhiteSpace(outbox.CandidateFamily) &&
        !string.IsNullOrWhiteSpace(outbox.ProducingRevision) &&
        outbox.ProducingModuleIdentity is not null &&
        !string.IsNullOrWhiteSpace(outbox.TargetScope);

    private static PendingTrustedTargetOutboxEnvelope ToTrustedTarget(OutboxEntry entry)
    {
        if (entry.TargetModuleIdentity is not null ||
            !string.IsNullOrEmpty(entry.TargetNeuronType) ||
            string.IsNullOrWhiteSpace(entry.OwnerId) ||
            string.IsNullOrWhiteSpace(entry.CandidateFamily) ||
            string.IsNullOrWhiteSpace(entry.ProducingRevision) ||
            entry.ProducingModuleIdentity is null ||
            string.IsNullOrWhiteSpace(entry.TargetScope))
        {
            throw new InvalidDataException(
                $"Committed trusted target payload '{entry.DeliveryId}' is missing immutable candidate provenance.");
        }

        return new PendingTrustedTargetOutboxEnvelope(
            entry.DeliveryId,
            entry.Kind,
            entry.ContractAlias,
            entry.PayloadFormat,
            entry.PayloadBase64,
            entry.OwnerId,
            CandidateFamilyId.Parse(entry.CandidateFamily),
            entry.ProducingRevision,
            entry.ProducingModuleIdentity,
            entry.TargetScope);
    }
}
