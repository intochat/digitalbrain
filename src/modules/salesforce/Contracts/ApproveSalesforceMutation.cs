using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Salesforce;

[GenerateSerializer]
[Alias("db.salesforce.approve-mutation")]
[Description("Session-owned request to execute a previously proposed Salesforce mutation")]
public sealed record ApproveSalesforceMutation(
    [property: Id(0)] SalesforceMutationApproval Approval) : RequestSynapse<SalesforceResponse>;
