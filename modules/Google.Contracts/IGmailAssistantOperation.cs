using Brain.Contracts;

namespace Google.Contracts;

public interface IGmailAssistantOperation : INeuronContract
{
    [NeuronContract(GoogleCapabilityIds.GmailInboxSummarize)]
    Task<GmailInboxSummaryReceipt> SummarizeAsync(GmailInboxSummaryRequest request);
}
