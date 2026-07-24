using DigitalBrain.Abstractions;

namespace DigitalBrain.Salesforce;

[Alias("db.salesforce")]
public interface ISalesforce : INeuron
{
    [Alias("ProposeAccountDescription")]
    Task<SalesforceAccountDescriptionMutation> ProposeAccountDescriptionAsync(
        CommandId commandId,
        NeuronId requester,
        string accountId,
        string description,
        CancellationToken cancellationToken);

    [Alias("ApproveAccountDescription")]
    Task<SalesforceAccountDescriptionMutation> ApproveAccountDescriptionAsync(
        SalesforceMutationApproval approval,
        SynapseDelivery approvalEvidence,
        CancellationToken cancellationToken);
}
