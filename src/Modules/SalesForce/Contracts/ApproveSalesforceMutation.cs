using DigitalBrain.Abstractions;

namespace DigitalBrain.Salesforce;

[GenerateSerializer]
[Alias("db.salesforce.approve-mutation")]
public sealed record ApproveSalesforceMutation(
    [property: Id(0)] SalesforceMutationApproval Approval) : RequestSynapse<SalesforceResponse>;
