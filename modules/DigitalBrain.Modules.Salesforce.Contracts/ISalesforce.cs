using DigitalBrain.Abstractions;

namespace DigitalBrain.Salesforce;

[Alias("db.salesforce")]
public partial interface ISalesforce : INeuron
{
    [Alias(nameof(ProposeAccountDescription))]
    Task<SalesforceAccountDescriptionMutation> ProposeAccountDescription(
        CommandId commandId,
        NeuronId requester,
        string accountId,
        string description,
        CancellationToken cancellationToken);

    [Alias(nameof(ApproveAccountDescription))]
    Task<SalesforceAccountDescriptionMutation> ApproveAccountDescription(
        SalesforceMutationApproval approval,
        SynapseDelivery approvalEvidence,
        CancellationToken cancellationToken);
}
