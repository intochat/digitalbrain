using DigitalBrain.Abstractions;

namespace DigitalBrain.Salesforce;

[GenerateSerializer]
[Alias("db.salesforce.account-description-mutation")]
public sealed record SalesforceAccountDescriptionMutation(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string AccountId,
    [property: Id(2)] string Description,
    [property: Id(3)] string Fingerprint,
    [property: Id(4)] SalesforceMutationState State);

[GenerateSerializer]
[Alias("db.salesforce.mutation-state")]
public enum SalesforceMutationState
{
    AwaitingApproval,
    Completed,
    OutcomeUncertain,
}
