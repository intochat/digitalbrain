using Brain.Contracts;

namespace Google.Contracts;

public interface IGmailNeuron : INeuronContract
{
    [NeuronContract(GoogleCapabilityIds.GmailMailboxRead)]
    Task<GmailMailboxPage> ReadMailboxAsync(GmailMailboxReadRequest request);

    [NeuronContract(GoogleCapabilityIds.GmailMessageRead)]
    Task<GmailMessage> ReadMessageAsync(GmailMessageReadRequest request);

    [NeuronContract(GoogleCapabilityIds.GmailSendPropose)]
    Task<NeuronReply<GmailSendProposal>> ProposeSendAsync(GmailSendProposalRequest request);

    [NeuronContract(GoogleCapabilityIds.GmailSendExecute)]
    Task<GmailSendResult> ExecuteSendAsync(GmailSendExecutionRequest request);
}
