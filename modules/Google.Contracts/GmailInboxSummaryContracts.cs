namespace Google.Contracts;

public sealed class GmailInboxSummaryRequest
{
    public GmailInboxSummaryRequest(
        int maximumMessages = 10,
        GmailSendProposalRequest? reply = null)
    {
        MaximumMessages = ContractGuard.Range(maximumMessages, nameof(maximumMessages), 1, 10);
        Reply = reply;
    }

    public int MaximumMessages { get; }
    public GmailSendProposalRequest? Reply { get; }
}

public sealed class GmailInboxSummaryReceipt
{
    public GmailInboxSummaryReceipt(
        int messageCount,
        string summary,
        long windowRevision)
    {
        MessageCount = ContractGuard.Range(messageCount, nameof(messageCount), 0, 10);
        Summary = ContractGuard.Bounded(summary, nameof(summary), 16_384);
        if (windowRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(windowRevision));
        WindowRevision = windowRevision;
    }

    public int MessageCount { get; }
    public string Summary { get; }
    public long WindowRevision { get; }
}
