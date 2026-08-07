namespace DigitalBrain.Product.Time;

public sealed record ProposalDeadline
{
    public ProposalDeadline(string proposalId, string proposalFingerprint, DateTimeOffset dueAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalFingerprint);
        if (dueAt == default)
        {
            throw new ArgumentException("A proposal deadline needs a due time.", nameof(dueAt));
        }

        ProposalId = proposalId.Trim();
        ProposalFingerprint = proposalFingerprint.Trim();
        DueAt = dueAt;
    }

    public string ProposalId { get; }

    public string ProposalFingerprint { get; }

    public DateTimeOffset DueAt { get; }
}
